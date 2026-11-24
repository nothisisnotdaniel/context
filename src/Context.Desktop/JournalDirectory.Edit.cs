using System.Security.Cryptography;
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
        private sealed record Change(Action Undo, Action Release, Action? Finish = null, bool Creation = false,
            bool FinishLast = false, Action? VerifyRetired = null);
        private readonly JournalDirectory source;
        private readonly JournalDirectory editor;
        private readonly string originalPath;
        private readonly Dictionary<string, Observation> selection;
        private SafeFileHandle? restoredNote;
        private readonly List<string> recoveryLocations = [];
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

        internal Edit(JournalDirectory source, NoteInfo note, JournalDirectory? editor = null)
        {
            this.source = source;
            this.editor = editor ?? source;
            originalPath = selected = note.Path;
            using FileStream file = source.OpenNote(note);
            entries[selected] = new(source.Stamp(file.SafeFileHandle, false), false);
            lock (source.original)
            {
                for (string? path = Path.GetDirectoryName(selected); path != source.root; path = Path.GetDirectoryName(path!))
                {
                    entries[path!] = new(source.observed[path!], true);
                }
            }
            selection = new(entries, StringComparer.Ordinal);
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
                    CaptureFile(page.Path, NoteStore.MaxEditBytes, page.Modified.ToUniversalTime());
                    IReadOnlyList<NoteVersion> versions = NoteHistory.BoundedPlainVersions(page.Path, edit);
                    histories[page.Path] = versions;
                    foreach (NoteVersion version in versions)
                    {
                        CaptureFile(Path.Combine(NoteHistory.FolderFor(page.Path), NoteHistory.FileName(version.Id, null)), NoteHistory.MaxVersionBytes);
                    }
                }
                observed = new(edit.entries, StringComparer.Ordinal);

                void CaptureFile(string path, long limit, DateTime? displayed = null)
                {
                    string key = edit.Key(path);
                    using SafeFileHandle file = edit.OpenEntry(key, false);
                    (long length, DateTime written) = FileVersion(file);
                    if (displayed.HasValue && displayed.Value != written)
                    {
                        throw Ineligible();
                    }
                    edit.entries[key] = edit.entries[key] with { Confirmed = (length, written), Hash = CopyAndHash(file, limit), Limit = limit };
                }
            }

            internal IReadOnlyDictionary<string, IReadOnlyList<NoteVersion>> Histories => histories;

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
                    if (known.Hash is not null)
                    {
                        _ = edit.Read(path, known.Limit);
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
                EntryIdentity stamp = source.Stamp(handle, directory);
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
                        if (source.Stamp(witness, directory) != stamp)
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
            entries[key] = new(source.Stamp(created, true), true);
            Expect(key, true);
        }

        public override void WriteNew(string path, byte[] bytes) => WriteNew(path, bytes, null);

        public override void Reserve(string path) => WriteNew(path, [], null, reservation: true);

        private void WriteNew(string path, byte[] bytes, DateTime? modified, bool reservation = false)
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
            FileId? createdId = null;
            changes.Add(new(() =>
            {
                if (!reservation)
                {
                    created.RollbackChecked();
                }
            }, () =>
            {
                if (reservation)
                {
                    created.Rollback();
                    entries.Remove(key);
                }
                created.Dispose();
                if (createdId is { } id && witnesses.Remove(id, out SafeFileHandle? witness))
                {
                    witness.Dispose();
                }
            }, Creation: true));
            using SafeFileHandle current = OpenHandle(leaf, false, folder, shareDelete: true);
            if (!created.Matches(folder, current))
            {
                throw Ineligible();
            }
            absent.Remove(key);
            entries[key] = new(source.Stamp(current, false), false, EditableNote.Hash(bytes), bytes.LongLength);
            createdId = entries[key].Identity.Id;
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
                if (!Matches(expected.Identity, source.Stamp(item, directory)))
                {
                    throw Ineligible();
                }
                MoveOwned(sourceFolder, oldName, destination, newName, directory, item, plainHistory);
                TrackTransfer(from, to, expected, sourceFolder, destination, item, discard, plainHistory);
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

        private void TrackTransfer(string from, string to, Observation expected, SafeFileHandle originalFolder,
            SafeFileHandle destination, SafeFileHandle item, bool discard, bool plainHistory)
        {
            string oldName = Path.GetFileName(from), newName = Path.GetFileName(to);
            bool removed = false, restored = false;
            changes.Add(new(Undo, () => { item.Dispose(); originalFolder.Dispose(); destination.Dispose(); },
                discard ? Finish : null, FinishLast: discard && from == selected, VerifyRetired: discard ? VerifyOriginal : null));

            void VerifyOriginal()
            {
                if ((expected.Hash is not null && CopyAndHash(item, expected.Limit) != expected.Hash)
                    || (expected.Confirmed is { } confirmed && FileVersion(item) != confirmed))
                {
                    throw Ineligible();
                }
            }

            void Finish()
            {
                if (source.RemovalAvailable?.Invoke(from) == false)
                {
                    throw Ineligible();
                }
                VerifyOriginal();
                RemoveAt(destination, newName, false, item, plainHistory);
                removed = true;
                entries.Remove(to);
            }

            void Undo()
            {
                if (restored)
                {
                    return;
                }
                try
                {
                    if (source.RecoveryAvailable?.Invoke(from) == false)
                    {
                        throw Ineligible();
                    }
                    EntryIdentity identity;
                    if (removed)
                    {
                        identity = RestoreCopy(originalFolder, oldName, item, plainHistory, out _);
                    }
                    else
                    {
                        MoveOwned(destination, newName, originalFolder, oldName, expected.Directory, item, plainHistory);
                        VerifyOwned(originalFolder, oldName, expected.Directory, item);
                        identity = source.Stamp(item, expected.Directory);
                    }
                    restored = true;
                    if (from == originalPath)
                    {
                        restoredNote = OpenHandle(oldName, false, originalFolder, shareDelete: true);
                        if (source.Stamp(restoredNote, false) != identity)
                        {
                            restoredNote.Dispose();
                            restoredNote = null;
                            throw Ineligible();
                        }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Keep a named, readable original even if the original name is occupied
                    // or its staged file was unlinked before the final check failed.
                    string? location = removed ? null : RecoveryLocation(destination, newName, expected.Directory, item);
                    if (location is not null)
                    {
                        recoveryLocations.Add(location);
                    }
                    else if (!expected.Directory)
                    {
                        RestoreCopy(originalFolder, TempName(plainHistory), item, plainHistory, out string copy);
                        recoveryLocations.Add(copy);
                    }
                    throw;
                }
            }

        }

        private static string? RecoveryLocation(SafeFileHandle folder, string name, bool directory, SafeFileHandle original)
        {
            try
            {
                using SafeFileHandle current = OpenHandle(name, directory, folder, shareDelete: true);
                if (Identity(current, directory) != Identity(original, directory))
                {
                    return null;
                }
                if (!directory)
                {
                    _ = CopyAndHash(current, long.MaxValue);
                }
                return CheckedPath(current, directory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private EntryIdentity RestoreCopy(SafeFileHandle parent, string name, SafeFileHandle item, bool plainHistory, out string location)
        {
            using SafeFileHandle folder = ReopenFolder(parent, shareDelete: false);
            string? digest = null;
            using CreatedFile restored = JournalDirectory.WriteNew(folder, name, stream =>
            {
                digest = CopyAndHash(item, long.MaxValue, stream);
                stream.Flush();
                File.SetLastWriteTimeUtc(stream.SafeFileHandle, File.GetLastWriteTimeUtc(item));
            }, plainHistory: plainHistory);
            using SafeFileHandle readable = OpenHandle(name, false, folder, shareDelete: true);
            if (!restored.Matches(folder, readable) || CopyAndHash(readable, long.MaxValue) != digest)
            {
                throw Ineligible();
            }
            location = CheckedPath(readable, false);
            return source.Stamp(readable, false);
        }

        // Read from the retained handle without taking ownership of it or changing its position.
        private static string CopyAndHash(SafeFileHandle file, long limit, Stream? copy = null)
        {
            long length = RandomAccess.GetLength(file);
            if (length > limit)
            {
                throw new FileTooLargeException();
            }
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[(int)Math.Min(length, 81920)];
            long offset = 0;
            while (offset < length)
            {
                int count = RandomAccess.Read(file, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - offset)), offset);
                if (count == 0)
                {
                    throw Ineligible();
                }
                hash.AppendData(buffer, 0, count);
                copy?.Write(buffer, 0, count);
                offset += count;
            }
            if (RandomAccess.GetLength(file) != length)
            {
                throw Ineligible();
            }
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        private void RemoveOwned(SafeFileHandle parent, string leaf, Observation expected)
        {
            using SafeFileHandle current = OpenHandle(leaf, expected.Directory, parent, shareDelete: true);
            if (!Matches(expected.Identity, source.Stamp(current, expected.Directory)))
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
            foreach (Change change in changes)
            {
                change.VerifyRetired?.Invoke();
            }
            VerifyFinalState();
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
        internal enum ConversionOutcome { Sealed, PlainHistoryLeft, Restored }

        internal ConversionOutcome FinishConversion(NoteInfo note, IReadOnlyList<string> versions, Action? beforeHistoryCleanup,
            out IReadOnlyList<string> recoveryFiles)
        {
            Dictionary<string, Observation> originals = versions.Prepend(note.Path).ToDictionary(Key, path => entries[Key(path)], StringComparer.Ordinal);
            try
            {
                return RetirePlaintext(note, versions, beforeHistoryCleanup, out recoveryFiles)
                    ? ConversionOutcome.Sealed : ConversionOutcome.PlainHistoryLeft;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Rollback();
                List<string> readable = [];
                try
                {
                    using Edit restored = new(editor, note);
                    foreach ((string path, Observation expected) in originals)
                    {
                        if (EditableNote.Hash(restored.Read(path, expected.Limit)) != expected.Hash)
                        {
                            throw Ineligible();
                        }
                        readable.Add(path);
                    }
                    restored.Verify();
                }
                catch (Exception check) when (check is IOException or UnauthorizedAccessException)
                {
                    throw new RecoveryFailure(readable);
                }
                recoveryFiles = [];
                return ConversionOutcome.Restored;
            }
        }

        private bool RetirePlaintext(NoteInfo note, IReadOnlyList<string> versions, Action? beforeHistoryCleanup,
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
                List<string> readable = [];
                foreach (NoteFileEntry entry in List(historyPath).Where(e => !e.Directory))
                {
                    bool recovery = IsRecoveryName(entry.Name);
                    if (recovery || entry.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    {
                        clean = false;
                    }
                    if (recovery)
                    {
                        try
                        {
                            using SafeFileHandle file = OpenEntry(Path.Combine(historyPath, entry.Name), false);
                            _ = CopyAndHash(file, NoteHistory.MaxVersionBytes);
                            readable.Add(entry.Name);
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                        {
                            // Cleanup is unconfirmed. Only identify copies actually read.
                        }
                    }
                }
                recoveryFiles = readable.Order(StringComparer.Ordinal).ToArray();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                clean = false;
            }
            return clean;

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

        // Earlier operations staged versions as .<guid>.tmp as well as .<guid>.tmp.md.
        // Their ownership cannot be proven after restart. Leave them for the user.
        private static bool IsRecoveryName(string name) => name.Length >= 37 && name[0] == '.'
            && name[33..] is ".tmp" or ".tmp.md" && Guid.TryParseExact(name.AsSpan(1, 32), "N", out _);

        internal sealed class RecoveryFailure : IOException
        {
            internal IReadOnlyList<string> Locations { get; }
            internal RecoveryFailure(IReadOnlyList<string> locations) => Locations = locations;
        }

        private void Rollback()
        {
            recoveryLocations.Clear();
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
            changes.Clear();
            if (restoredNote is not null || selection.Values.Any(entry => entry.Directory && entry.Identity.ChangeTime))
            {
                try
                {
                    RefreshBinding();
                    if (keepCreations && restoredNote is not null)
                    {
                        recoveryLocations.Add(CheckedPath(restoredNote, false));
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    keepCreations = true;
                    if (restoredNote is not null)
                    {
                        recoveryLocations.Add(CheckedPath(restoredNote, false));
                    }
                }
            }
            if (keepCreations)
            {
                throw new RecoveryFailure(recoveryLocations.ToArray());
            }
        }

        private void RefreshBinding()
        {
            entries.Clear();
            absent.Clear();
            foreach ((string path, Observation original) in selection)
            {
                entries.Add(path, original);
            }
            if (restoredNote is not null)
            {
                entries[originalPath] = new(source.Stamp(restoredNote, false), false);
            }
            originalRoot = pinnedHistory = false;
            using SafeFileHandle current = OpenEntry(originalPath, false);
            lock (editor.original)
            {
                if (editor.original.IsClosed || selection.Any(pair => !editor.observed.TryGetValue(pair.Key, out EntryIdentity known)
                    || known != pair.Value.Identity))
                {
                    throw Ineligible();
                }
                foreach (string path in selection.Keys)
                {
                    editor.observed[path] = entries[path].Identity;
                }
            }
        }

        public override void Dispose()
        {
            try
            {
                if (!committed && changes.Count > 0)
                {
                    Rollback();
                }
            }
            finally
            {
                restoredNote?.Dispose();
                historyFolder?.Dispose();
                foreach (SafeFileHandle witness in witnesses.Values)
                {
                    witness.Dispose();
                }
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
