using Microsoft.Win32.SafeHandles;

namespace Context;

internal sealed partial class JournalDirectory
{
    public NoteFileEdit BeginEdit(NoteInfo note) => new Edit(this, note);

    // Each step traverses from the bound root and compares the entries already observed.
    // Undo records keep the actual parent directories, independent of the configured name.
    internal sealed class Edit : NoteFileEdit
    {
        private sealed record Observation(EntryIdentity Identity, bool Directory, string? Hash = null, long Limit = 0,
            (long Length, DateTime Written)? Confirmed = null);
        private sealed record Change(Action Undo, Action Release, Action? Finish = null, bool Creation = false, bool FinishLast = false);
        private readonly JournalDirectory source;
        private readonly Dictionary<string, Observation> entries = new(StringComparer.Ordinal);
        private readonly Dictionary<FileId, SafeFileHandle> witnesses = [];
        private readonly HashSet<string> absent = new(StringComparer.Ordinal);
        private readonly List<Change> changes = [];
        private string selected;
        private bool committed;
        private SafeFileHandle? historyFolder;
        private string? historyPath;
        private bool originalRoot;
        private bool pinnedHistory;

        internal Edit(JournalDirectory source, NoteInfo note)
        {
            this.source = source;
            selected = note.Path;
            using FileStream file = source.OpenNote(note);
            entries[selected] = new(Stamp(file.SafeFileHandle, false), false);
            lock (source.original)
            {
                for (string? path = Path.GetDirectoryName(selected); path != source.root; path = Path.GetDirectoryName(path!))
                {
                    entries[path!] = new(source.observed[path!], true);
                }
            }
        }

        // The question carries values only. Neither a file nor a directory stays open
        // while the user decides. Confirmation opens a fresh edit and checks those values.
        internal sealed class DeleteSelection
        {
            private readonly FileId root;
            private readonly Dictionary<string, Observation> observed;
            private readonly Dictionary<string, IReadOnlyList<NoteVersion>> histories = new(StringComparer.Ordinal);

            internal DeleteSelection(Edit edit, IEnumerable<NoteInfo> pages)
            {
                root = edit.source.id;
                foreach (NoteInfo page in pages)
                {
                    CaptureFile(page.Path, page.Modified.ToUniversalTime());
                    IReadOnlyList<NoteVersion> versions = NoteHistory.ListVersions(page.Path, edit, ".md");
                    histories[page.Path] = versions;
                    foreach (NoteVersion version in versions)
                    {
                        CaptureFile(Path.Combine(NoteHistory.FolderFor(page.Path), NoteHistory.FileName(version.Id, null)));
                    }
                }
                observed = new(edit.entries, StringComparer.Ordinal);

                void CaptureFile(string path, DateTime? displayed = null)
                {
                    string key = edit.Key(path);
                    using SafeFileHandle file = edit.OpenEntry(key, false);
                    (long length, DateTime written) = FileVersion(file);
                    if (displayed.HasValue && displayed.Value != written)
                    {
                        throw Ineligible();
                    }
                    edit.entries[key] = edit.entries[key] with { Confirmed = (length, written) };
                }
            }

            internal IReadOnlySet<string> Pages => histories.Keys.ToHashSet(StringComparer.Ordinal);

            internal void Verify(Edit edit)
            {
                if (edit.source.id != root)
                {
                    throw Ineligible();
                }
                foreach ((string path, Observation known) in observed)
                {
                    edit.entries[path] = known;
                    edit.Expect(path, known.Directory);
                    if (known.Identity != edit.entries[path].Identity)
                    {
                        throw Ineligible();
                    }
                }
                foreach ((string path, IReadOnlyList<NoteVersion> versions) in histories)
                {
                    if (!NoteHistory.ListVersions(path, edit, ".md").SequenceEqual(versions))
                    {
                        throw Ineligible();
                    }
                }
            }
        }

        private string Key(string path)
        {
            string root = source.root;
            if (OperatingSystem.IsWindows())
            {
                root = NoteHistory.ExtendedWindowsPath(root);
                path = NoteHistory.ExtendedWindowsPath(path);
            }
            string relative = Path.GetRelativePath(root, path);
            if (relative == ".")
            {
                return source.root;
            }
            if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar).Any(p => p is "" or ".." or "."))
            {
                throw Ineligible();
            }
            return Path.Combine(source.root, relative);
        }

        private void CheckAvailable(string path)
        {
            if (!(originalRoot && path == source.root) && source.EntryAvailable?.Invoke(path) == false)
            {
                throw Ineligible();
            }
        }

        private static (long Length, DateTime Written) FileVersion(SafeFileHandle file) =>
            (RandomAccess.GetLength(file), File.GetLastWriteTimeUtc(file));

        private SafeFileHandle OpenEntry(string path, bool directory, bool enumerate = false)
        {
            CheckAvailable(path);
            if (pinnedHistory && path == historyPath && historyFolder is not null)
            {
                return ReopenFolder(historyFolder, enumerate: enumerate);
            }
            if (path == source.root)
            {
                return originalRoot ? ReopenFolder(source.original, enumerate: enumerate) : source.OpenCurrent(enumerate, shareDelete: true);
            }
            using SafeFileHandle parent = OpenEntry(Path.GetDirectoryName(path)!, true);
            SafeFileHandle handle;
            try
            {
                handle = OpenHandle(Path.GetFileName(path), directory, parent, shareDelete: true, enumerate: enumerate);
            }
            catch (FileNotFoundException)
            {
                if (entries.ContainsKey(path))
                {
                    throw Ineligible();
                }
                absent.Add(path);
                throw;
            }
            try
            {
                EntryIdentity stamp = Stamp(handle, directory);
                if (absent.Contains(path))
                {
                    throw Ineligible();
                }
                if (entries.TryGetValue(path, out Observation? known))
                {
                    if (known.Directory != directory || !Matches(known.Identity, stamp)
                        || (known.Confirmed is { } confirmed && FileVersion(handle) != confirmed))
                    {
                        throw Ineligible();
                    }
                    entries[path] = known with { Identity = stamp };
                }
                else
                {
                    entries[path] = new(stamp, directory);
                }
                // On a volume without birth times, our own rename changes ctime.
                // Pin the observed inode for this edit only, so it cannot be recycled.
                if (stamp.ChangeTime && !witnesses.ContainsKey(stamp.Id))
                {
                    SafeFileHandle witness = OpenHandle(Path.GetFileName(path), directory, parent, shareDelete: true);
                    try
                    {
                        if (Stamp(witness, directory) != stamp)
                        {
                            throw Ineligible();
                        }
                        witnesses[stamp.Id] = witness;
                    }
                    catch
                    {
                        witness.Dispose();
                        throw;
                    }
                }
                return handle;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        private bool Matches(EntryIdentity expected, EntryIdentity current) => expected == current
            || (expected.ChangeTime && current.Id == expected.Id && witnesses.ContainsKey(expected.Id));

        public override void Expect(string path, bool directory = false)
        {
            using SafeFileHandle expected = OpenEntry(Key(path), directory);
        }

        public override byte[] Read(string path, long limit)
        {
            string key = Key(path);
            using FileStream stream = new(OpenEntry(key, false), FileAccess.Read);
            byte[] bytes = BoundedFile.Read(stream, limit);
            string hash = EditableNote.Hash(bytes);
            Observation known = entries[key];
            if (known.Hash is not null && known.Hash != hash)
            {
                throw Ineligible();
            }
            entries[key] = known with { Hash = hash, Limit = limit };
            return bytes;
        }

        public override IReadOnlyList<NoteFileEntry> List(string folder)
        {
            string key = Key(folder);
            SafeFileHandle handle;
            try
            {
                handle = OpenEntry(key, true, enumerate: true);
            }
            catch (FileNotFoundException) when (!entries.ContainsKey(key))
            {
                return [];
            }
            using (handle)
            {
                Listing listing = source.ListDirectory(handle, key, strict: true);
                return [.. listing.Folders.Select(n => new NoteFileEntry(n, true, 0)),
                    .. listing.Files.Select(n => new NoteFileEntry(n, false, listing.Length(n)))];
            }
        }

        public override void EnsureFolder(string path)
        {
            string key = Key(path);
            try
            {
                Expect(key, true);
                return;
            }
            catch (FileNotFoundException) when (!entries.ContainsKey(key))
            {
                // Create one component in its verified parent.
            }
            string parent = Path.GetDirectoryName(key)!;
            EnsureFolder(parent);
            using SafeFileHandle folder = OpenEntry(parent, true);
            string leaf = Path.GetFileName(key);
            using SafeFileHandle working = ReopenFolder(folder, shareDelete: false);
            string temp = TempName();
            SafeFileHandle held = ReopenFolder(working);
            SafeFileHandle? created = null;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    if (!CreateDirectoryW(Path.Combine(CheckedPath(working, true), temp), IntPtr.Zero))
                    {
                        throw Ineligible();
                    }
                }
                else if (UnixMakeDirectoryAt(working, temp, (uint)PrivateFile.FolderBits) != 0)
                {
                    throw Ineligible();
                }
                created = OpenHandle(temp, true, working, shareDelete: true);
                MoveOwned(working, temp, working, leaf, true, created);
            }
            catch
            {
                try
                {
                    if (created is not null)
                    {
                        RemoveAt(working, temp, true, created);
                    }
                }
                finally
                {
                    held.Dispose();
                    created?.Dispose();
                }
                throw;
            }
            changes.Add(new(() => RemoveAt(held, leaf, true, created),
                () => { created.Dispose(); held.Dispose(); }, Creation: true));
            absent.Remove(key);
            entries[key] = new(Stamp(created, true), true);
            Expect(key, true);
        }

        public override void WriteNew(string path, byte[] bytes) => WriteNew(path, bytes, null);

        private void WriteNew(string path, byte[] bytes, DateTime? modified)
        {
            string key = Key(path);
            using SafeFileHandle folder = OpenEntry(Path.GetDirectoryName(key)!, true);
            // Hold the Windows parent in place during private-file creation as well as publication.
            using SafeFileHandle working = ReopenFolder(folder, shareDelete: false);
            string leaf = Path.GetFileName(key);
            CreatedFile created = JournalDirectory.WriteNew(working, leaf, stream =>
            {
                stream.Write(bytes);
                stream.Flush();
                if (modified is { } time)
                {
                    File.SetLastWriteTimeUtc(stream.SafeFileHandle, time);
                }
            }, () => CheckAvailable(key));
            changes.Add(new(created.Rollback, created.Dispose, Creation: true));
            using SafeFileHandle current = OpenHandle(leaf, false, folder, shareDelete: true);
            if (!created.Matches(folder, current))
            {
                throw Ineligible();
            }
            absent.Remove(key);
            entries[key] = new(Stamp(current, false), false, EditableNote.Hash(bytes), bytes.LongLength);
        }

        public override void PublishNote(string from, string to, byte[]? rewritten)
        {
            byte[] bytes = rewritten ?? Read(from, NoteStore.MaxEditBytes);
            using SafeFileHandle original = OpenEntry(Key(from), false);
            DateTime modified = File.GetLastWriteTimeUtc(original);
            // Keep the original identity in its own directory until the final removal.
            // Recovering the selection never needs to read the published note.
            bool sameSpellingIgnoringCase = string.Equals(from, to, StringComparison.OrdinalIgnoreCase);
            if (sameSpellingIgnoringCase)
            {
                Discard(from);
            }
            WriteNew(to, bytes, rewritten is null ? modified : null);
            if (sameSpellingIgnoringCase)
            {
                absent.Remove(Key(from));
            }
            else
            {
                Discard(from);
            }
            selected = Key(to);
        }

        public override void Relocate(string from, string to, bool directory = false) =>
            Transfer(Key(from), Key(to), directory, discard: false);

        public override void Discard(string path)
        {
            string key = Key(path);
            Transfer(key, Path.Combine(Path.GetDirectoryName(key)!, TempName()), false, discard: true);
        }

        private void Transfer(string from, string to, bool directory, bool discard, bool plainHistory = false)
        {
            Expect(from, directory);
            Observation expected = entries[from];
            string oldName = Path.GetFileName(from), newName = Path.GetFileName(to);
            SafeFileHandle sourceFolder = OpenEntry(Path.GetDirectoryName(from)!, true);
            SafeFileHandle? destination = null;
            SafeFileHandle? item = null;
            try
            {
                destination = OpenEntry(Path.GetDirectoryName(to)!, true);
                item = OpenHandle(oldName, directory, sourceFolder, shareDelete: true);
                if (!Matches(expected.Identity, Stamp(item, directory)))
                {
                    throw Ineligible();
                }
                MoveOwned(sourceFolder, oldName, destination, newName, directory, item, plainHistory);
                bool removed = false, restoredName = false;
                void Undo()
                {
                    if (restoredName)
                    {
                        return;
                    }
                    if (!removed)
                    {
                        if (source.RecoveryAvailable?.Invoke(from) == false)
                        {
                            throw Ineligible();
                        }
                        VerifyOwned(destination, newName, directory, item);
                        MoveOwned(destination, newName, sourceFolder, oldName, directory, item, plainHistory);
                        VerifyOwned(sourceFolder, oldName, directory, item);
                        restoredName = true;
                        return;
                    }
                    // A later commit check failed after unlink. The retained file is
                    // still readable. Restore its bytes privately, without overwriting.
                    using SafeFileHandle folder = ReopenFolder(sourceFolder, shareDelete: false);
                    using FileStream original = new(item, FileAccess.Read);
                    using CreatedFile restored = JournalDirectory.WriteNew(folder, oldName, stream =>
                    {
                        original.CopyTo(stream);
                        stream.Flush();
                        File.SetLastWriteTimeUtc(stream.SafeFileHandle, File.GetLastWriteTimeUtc(original.SafeFileHandle));
                    }, plainHistory: plainHistory);
                }
                void Finish()
                {
                    if (source.RemovalAvailable?.Invoke(from) == false)
                    {
                        throw Ineligible();
                    }
                    RemoveAt(destination, newName, false, item, plainHistory);
                    removed = true;
                }
                changes.Add(new(Undo, () => { item.Dispose(); sourceFolder.Dispose(); destination.Dispose(); },
                    discard ? Finish : null, FinishLast: discard && from == selected));
            }
            catch
            {
                item?.Dispose();
                sourceFolder.Dispose();
                destination?.Dispose();
                throw;
            }
            string prefix = from + Path.DirectorySeparatorChar;
            foreach (string key in entries.Keys.Where(k => k == from || k.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            {
                entries[to + key[from.Length..]] = entries[key];
                entries.Remove(key);
            }
            foreach (string child in absent.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            {
                absent.Remove(child);
                absent.Add(to + child[from.Length..]);
            }
            absent.Remove(to);
            // On case-insensitive volumes the old spelling now resolves to the moved entry.
            if (!string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                absent.Add(from);
            }
            if (!discard && selected == from)
            {
                selected = to;
            }
            // The undo now owns both parent handles, even if the first reopen fails.
            Expect(to, directory);
        }

        private void RemoveOwned(SafeFileHandle parent, string leaf, Observation expected)
        {
            using SafeFileHandle current = OpenHandle(leaf, expected.Directory, parent, shareDelete: true);
            if (!Matches(expected.Identity, Stamp(current, expected.Directory)))
            {
                throw Ineligible();
            }
            RemoveAt(parent, leaf, expected.Directory, current);
        }

        public override void Verify()
        {
            using SafeFileHandle current = source.OpenCurrent();
            foreach ((string path, Observation observed) in entries.ToArray())
            {
                if (observed.Hash is not null)
                {
                    _ = Read(path, observed.Limit);
                }
                else
                {
                    Expect(path, observed.Directory);
                }
            }
            foreach (string path in absent.ToArray())
            {
                RequireAbsent(path);
            }
        }

        private void RequireAbsent(string path)
        {
            using SafeFileHandle folder = OpenEntry(Path.GetDirectoryName(path)!, true);
            try
            {
                using SafeFileHandle occupant = OpenHandle(Path.GetFileName(path), false, folder, shareDelete: true);
                throw Ineligible();
            }
            catch (FileNotFoundException)
            {
                // Only the filesystem's "missing" result confirms removal.
            }
        }

        public override void Commit()
        {
            PrepareCommit();
            Verify();
            foreach (Change change in changes.OrderBy(change => change.FinishLast))
            {
                change.Finish?.Invoke();
            }
            committed = true;
            lock (source.original)
            {
                if (entries.TryGetValue(selected, out Observation? note))
                {
                    source.observed[selected] = note.Identity;
                    for (string? p = Path.GetDirectoryName(selected); p != source.root; p = Path.GetDirectoryName(p!))
                    {
                        source.observed[p!] = entries[p!].Identity;
                    }
                }
            }
            ReleaseChanges();
        }

        public override void RemoveEmpty(string folder)
        {
            string key = Key(folder);
            try
            {
                if (!entries.TryGetValue(key, out Observation? known) || !known.Directory || List(key).Count != 0)
                {
                    return;
                }
                using SafeFileHandle parent = OpenEntry(Path.GetDirectoryName(key)!, true);
                RemoveOwned(parent, Path.GetFileName(key), known);
                entries.Remove(key);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A nonempty or changed directory stays.
            }
        }

        // Conversion keeps the original version directory through the deletion of the plain note.
        // A missing directory is rechecked against the original root during final enumeration.
        internal bool FinishConversion(NoteInfo note, IReadOnlyList<string> versions, Action? beforeHistoryCleanup,
            out IReadOnlyList<string> recoveryFiles)
        {
            string notePath = note.Path;
            recoveryFiles = [];
            historyPath = Key(NoteHistory.FolderFor(notePath));
            if (entries.ContainsKey(historyPath))
            {
                historyFolder = OpenEntry(historyPath, true);
            }
            Discard(notePath);
            Change plainNote = changes[^1];
            Verify();
            beforeHistoryCleanup?.Invoke();

            // The plain name has gone, but the original is recoverable until the complete
            // publication is verified. From here on, cleanup belongs to the original store.
            originalRoot = true;
            VerifyPublication();
            bool clean = true;
            HashSet<FileId> versionIds = versions.Select(path => entries[Key(path)].Identity.Id).ToHashSet();
            int firstVersion = changes.Count;
            pinnedHistory = true;
            try
            {
                foreach (string version in versions)
                {
                    _ = Read(version, NoteHistory.MaxVersionBytes);
                    // If both removal and recovery fail, every possible staging name
                    // is still Markdown in the original history directory.
                    Transfer(Key(version), Path.Combine(historyPath, TempName(plainHistory: true)), false, true, plainHistory: true);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                clean = false;
            }
            pinnedHistory = false;
            VerifyPublication();
            for (int i = firstVersion; i < changes.Count; i++)
            {
                try
                {
                    changes[i].Finish!();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    clean = false;
                    try
                    {
                        changes[i].Undo();
                    }
                    catch (Exception restore) when (restore is IOException or UnauthorizedAccessException)
                    {
                        // A newer version keeps its name. Report the unverified cleanup.
                    }
                }
            }
            VerifyPublication();
            plainNote.Finish!();
            VerifyPublication();
            committed = true;
            // Windows completes an owned deletion when its last file handle closes.
            // Enumerate after releasing those handles while the original directory is still bound.
            ReleaseChanges();
            try
            {
                NoteFileEntry[] readable = List(historyPath).Where(v => !v.Directory && v.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)).ToArray();
                clean &= readable.Length == 0;
                recoveryFiles = readable.Where(v => v.Name.EndsWith(".tmp.md", StringComparison.Ordinal)
                    && IsOriginalVersion(v.Name)).Select(v => v.Name).Order(StringComparer.Ordinal).ToArray();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                clean = false;
            }
            return clean;

            bool IsOriginalVersion(string name)
            {
                using SafeFileHandle file = OpenEntry(Path.Combine(historyPath, name), false);
                return versionIds.Contains(Identity(file, false));
            }

            void VerifyPublication()
            {
                RequireAbsent(notePath);
                NoteHistory.RequirePlainOwner(notePath, this);
                if (NoteStore.HasSelectedSubpages(note, this))
                {
                    throw Ineligible();
                }
                foreach (string key in entries.Keys.Where(path => path.EndsWith(NoteStore.SensitiveExtension, StringComparison.Ordinal)).ToArray())
                {
                    _ = Read(key, NoteHistory.MaxVersionBytes);
                }
            }
        }

        public override void Dispose()
        {
            bool keepCreations = false;
            for (int index = changes.Count - 1; index >= 0; index--)
            {
                Change change = changes[index];
                try
                {
                    if (!committed && !(keepCreations && change.Creation))
                    {
                        change.Undo();
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Preserve published copies if an original could not be put back.
                    keepCreations = true;
                }
                finally
                {
                    change.Release();
                }
            }
            historyFolder?.Dispose();
            foreach (SafeFileHandle witness in witnesses.Values)
            {
                witness.Dispose();
            }
        }

        private void ReleaseChanges()
        {
            foreach (Change change in changes)
            {
                change.Release();
            }
            changes.Clear();
        }
    }
}
