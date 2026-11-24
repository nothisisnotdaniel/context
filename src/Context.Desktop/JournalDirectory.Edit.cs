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
        private sealed record Change(Action Undo, Action Release, Action? Finish = null, bool FinishLast = false,
            Action? VerifyRetired = null, string? From = null, string? StagedPath = null,
            string? CreatedPath = null, Func<RecoveryLocation?>? Location = null);
        private sealed class Original
        {
            public Original(byte[] bytes, Observation expected, string ownerPath)
            {
                Bytes = bytes;
                Expected = expected;
                OwnerPath = ownerPath;
            }

            public byte[] Bytes { get; }
            public Observation Expected { get; }
            public string OwnerPath { get; }
        }

        private sealed class RecoveryLocation
        {
            public required string Key { get; init; }
            public required string Path { get; init; }
            public required FileId Folder { get; init; }
            public required FileId Item { get; init; }
            public bool Directory { get; init; }
            public string? Hash { get; init; }
            public long Limit { get; init; }
        }
        private readonly JournalDirectory source;
        private readonly JournalDirectory editor;
        private readonly NoteInfo originalNote;
        private readonly string originalPath;
        private readonly Dictionary<string, Observation> selection;
        private readonly Dictionary<string, Original> originals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Observation> originalFolders = new(StringComparer.Ordinal);
        private readonly Dictionary<string, EntryIdentity> restoredEntries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RecoveryLocation> recoveryLocations = new(StringComparer.Ordinal);
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
            originalNote = note;
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

        public override byte[] ReadOriginal(string path, long limit, string? ownerPath = null)
        {
            string key = Key(path);
            if (originals.TryGetValue(key, out Original? kept))
            {
                return kept.Bytes;
            }
            byte[] bytes = Read(key, limit);
            originals[key] = new(bytes, entries[key], ownerPath ?? originalPath);
            for (string? folder = Path.GetDirectoryName(key); folder != source.root; folder = Path.GetDirectoryName(folder!))
            {
                originalFolders[folder!] = entries[folder!];
            }
            return bytes;
        }

        // Conversion calls this while the plain file is still untouched. If another
        // editor saved in place, rollback must verify and preserve that newer revision.
        internal bool PlainNoteUnchanged()
        {
            Original prior = originals[originalPath];
            using FileStream file = new(OpenEntry(originalPath, false), FileAccess.Read);
            byte[] bytes = BoundedFile.Read(file, prior.Expected.Limit);
            string hash = EditableNote.Hash(bytes);
            if (hash == prior.Expected.Hash)
            {
                return true;
            }
            originals[originalPath] = new(bytes, entries[originalPath] with { Hash = hash }, prior.OwnerPath);
            return false;
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
            FileId? createdId = null;
            changes.Add(new(() => RemoveAt(held, leaf, true, created),
                () =>
                {
                    created.Dispose();
                    held.Dispose();
                    if (createdId is { } id)
                    {
                        ReleaseWitness(id);
                    }
                }, CreatedPath: key));
            absent.Remove(key);
            entries[key] = new(source.Stamp(created, true), true);
            createdId = entries[key].Identity.Id;
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
            string hash = EditableNote.Hash(bytes);
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
                if (createdId is { } id)
                {
                    ReleaseWitness(id);
                }
            }, CreatedPath: reservation ? null : key,
                Location: () => NamedLocation(key, created.Parent, leaf, false, created.Handle, hash, bytes.LongLength)));
            using SafeFileHandle current = OpenHandle(leaf, false, folder, shareDelete: true);
            if (!created.Matches(folder, current))
            {
                throw Ineligible();
            }
            absent.Remove(key);
            entries[key] = new(source.Stamp(current, false), false, hash, bytes.LongLength);
            createdId = entries[key].Identity.Id;
        }

        private void ReleaseWitness(FileId id)
        {
            if (witnesses.Remove(id, out SafeFileHandle? witness))
            {
                witness.Dispose();
            }
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
            Transfer(key, Path.Combine(Path.GetDirectoryName(key)!, RecoveryName(key)), false, discard: true);
        }

        // A kept snapshot can outlive the editor and later saves. Its namespace follows
        // the exact per-note history key, including the stored spelling of legacy titles.
        private string RecoveryPrefix(string history) => ".recovery-" + EditableNote.Hash(
            System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(source.root, Key(history)).Replace(Path.DirectorySeparatorChar, '/'))) + "-";

        private string RecoveryName(string path)
        {
            string owner = originals.TryGetValue(path, out Original? kept) ? kept.OwnerPath : originalPath;
            return RecoveryPrefix(NoteHistory.FolderFor(owner)) + Guid.NewGuid().ToString("N") + ".tmp";
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
                discard ? Finish : null, FinishLast: discard && from == selected, VerifyRetired: discard ? VerifyOriginal : null,
                From: from, StagedPath: discard ? to : null));

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
                        identity = RestoreCopy(originalFolder, oldName, item, plainHistory);
                    }
                    else
                    {
                        MoveOwned(destination, newName, originalFolder, oldName, expected.Directory, item, plainHistory);
                        VerifyOwned(originalFolder, oldName, expected.Directory, item);
                        identity = source.Stamp(item, expected.Directory);
                    }
                    restored = true;
                    restoredEntries[from] = identity;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Keep a named, readable original even if the original name is occupied
                    // or its staged file was unlinked before the final check failed.
                    RecoveryLocation? location = removed ? null : NamedLocation(to, destination, newName, expected.Directory, item, expected.Hash, expected.Limit);
                    if (location is not null)
                    {
                        recoveryLocations[from] = location;
                    }
                    else if (!expected.Directory)
                    {
                        string leaf = plainHistory ? TempName(true) : RecoveryName(from);
                        EntryIdentity copy = RestoreCopy(originalFolder, leaf, item, plainHistory);
                        using SafeFileHandle file = OpenHandle(leaf, false, originalFolder, shareDelete: true);
                        if (source.Stamp(file, false) == copy && NamedLocation(Path.Combine(Path.GetDirectoryName(from)!, leaf),
                            originalFolder, leaf, false, file, expected.Hash ?? CopyAndHash(item, long.MaxValue), long.MaxValue) is { } verified)
                        {
                            recoveryLocations[from] = verified;
                        }
                    }
                    throw;
                }
            }

        }

        // Resolve the containing directory, then reopen the reported name there. A retained
        // handle can still be readable even when its former name cannot be used for recovery.
        private RecoveryLocation? NamedLocation(string key, SafeFileHandle folder, string name, bool directory,
            SafeFileHandle original, string? hash, long limit)
        {
            try
            {
                RecoveryLocation named = new()
                {
                    Key = key, Path = Path.Combine(CheckedPath(folder, true), name),
                    Folder = DirectoryId(folder), Item = Identity(original, directory), Directory = directory,
                    Hash = directory ? null : hash ?? CopyAndHash(original, long.MaxValue), Limit = hash is null ? long.MaxValue : limit,
                };
                return CanReopen(named) ? named : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private bool CanReopen(RecoveryLocation named)
        {
            try
            {
                for (string key = named.Key; key != source.root; key = Path.GetDirectoryName(key)!)
                {
                    CheckAvailable(key);
                }
                using SafeFileHandle folder = OpenHandle(Path.GetDirectoryName(named.Path)!, true, shareDelete: true);
                if (DirectoryId(folder) != named.Folder)
                {
                    return false;
                }
                using SafeFileHandle current = OpenHandle(Path.GetFileName(named.Path), named.Directory, folder, shareDelete: true);
                return Identity(current, named.Directory) == named.Item && (named.Directory || CopyAndHash(current, named.Limit) == named.Hash);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private EntryIdentity RestoreCopy(SafeFileHandle parent, string name, SafeFileHandle item, bool plainHistory)
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
            try
            {
                return RetirePlaintext(note, versions, beforeHistoryCleanup, out recoveryFiles)
                    ? ConversionOutcome.Sealed : ConversionOutcome.PlainHistoryLeft;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Rollback();
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
            absent.Remove(historyPath);
            absent.Remove(Path.GetDirectoryName(historyPath)!);
            List<string> readable = [];
            clean &= InventoryRecovery(historyPath, conversion: true, readable);
            recoveryFiles = readable;
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

        internal string? RecoveryFolder { get; private set; }

        // Earlier releases used anonymous temporary names. They cannot be attributed
        // with certainty, so leave them in place and surface unconfirmed cleanup.
        private bool InventoryRecovery(string history, bool conversion, List<string> readable)
        {
            string level = Path.GetDirectoryName(Path.GetDirectoryName(history)!)!;
            string prefix = RecoveryPrefix(history);
            string attachments = Path.Combine(level, NoteAttachments.FolderName);
            string[] folders = [history, level, attachments, source.root];
            bool clean = true;
            foreach (string key in folders.Distinct(StringComparer.Ordinal))
            {
                try
                {
                    string[] names = List(key).Where(e => !e.Directory && MatchesRecovery(e.Name)
                        && !changes.Any(c => c.StagedPath == Path.Combine(key, e.Name))).Select(e => e.Name).ToArray();
                    if (names.Length == 0)
                    {
                        continue;
                    }
                    clean = false;
                    using SafeFileHandle folder = OpenEntry(key, true);
                    RecoveryFolder ??= VerifiedFolder(folder);
                    readable.AddRange(ReadableRecovery(folder, key, names));
                }
                catch (Exception e) when (conversion && e is IOException or UnauthorizedAccessException)
                {
                    clean = false;
                    if (key == history && historyFolder is not null)
                    {
                        try
                        {
                            CheckAvailable(key);
                            RecoveryFolder ??= VerifiedFolder(historyFolder);
                            using SafeFileHandle held = ReopenFolder(historyFolder, enumerate: true);
                            readable.AddRange(ReadableRecovery(held, key, source.ListDirectory(held, key, strict: true).Files.Where(MatchesRecovery)));
                        }
                        catch (Exception unavailable) when (unavailable is IOException or UnauthorizedAccessException)
                        {
                            // Continue checking the other recovery directories in this store.
                        }
                    }
                }

                bool MatchesRecovery(string name) =>
                    (name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".tmp", StringComparison.Ordinal)
                        && Guid.TryParseExact(name.AsSpan(prefix.Length, name.Length - prefix.Length - 4), "N", out _))
                    || ((key == level || key == history || key == attachments) && IsRecoveryName(name))
                    || (conversion && key == history && name.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
            }
            return clean;
        }

        private static string VerifiedFolder(SafeFileHandle folder)
        {
            string path = CheckedPath(folder, true);
            using SafeFileHandle named = OpenHandle(path, true, shareDelete: true);
            return DirectoryId(named) == DirectoryId(folder) ? path : throw Ineligible();
        }

        private IReadOnlyList<string> ReadableRecovery(SafeFileHandle folder, string key, IEnumerable<string> names)
        {
            List<string> readable = [];
            foreach (string name in names.Order(StringComparer.Ordinal))
            {
                try
                {
                    CheckAvailable(Path.Combine(key, name));
                    using SafeFileHandle file = OpenHandle(name, false, folder, shareDelete: true);
                    string hash = CopyAndHash(file, NoteAttachments.MaxBytes);
                    if (NamedLocation(Path.Combine(key, name), folder, name, false, file, hash, NoteAttachments.MaxBytes) is { } location)
                    {
                        readable.Add(location.Path);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // An unverified file can prevent cleanup, but is never offered for recovery.
                }
            }
            return readable;
        }

        public override void RequireResolvedHistory(string folder)
        {
            List<string> readable = [];
            if (!InventoryRecovery(Key(folder), conversion: false, readable))
            {
                throw new RecoveryFailure(readable.Count == 0 && RecoveryFolder is { } path ? [path] : readable, earlierHistory: true);
            }
        }

        internal sealed class RecoveryFailure : IOException
        {
            internal IReadOnlyList<string> Locations { get; }
            internal bool EarlierHistory { get; }
            internal RecoveryFailure(IReadOnlyList<string> locations, bool earlierHistory = false)
            {
                Locations = locations;
                EarlierHistory = earlierHistory;
            }
        }

        private void Rollback()
        {
            HashSet<string> restoreNames = changes.Select(change => change.From).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            Change[] reverse = changes.AsEnumerable().Reverse().ToArray();
            bool Deferred(Change change) => change.CreatedPath is { } path && !restoreNames.Contains(path);
            bool incomplete = false;
            foreach (Change change in reverse.Where(change => !Deferred(change)))
            {
                try
                {
                    change.Undo();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    incomplete = true;
                }
                finally
                {
                    change.Release();
                }
            }
            bool keepPublications = incomplete;
            // A write during staging can be the only readable copy, even when no
            // original has moved. Keep the captured bytes until cleanup and the final
            // editor open have both finished. Check the actual bound store throughout.
            CheckOriginals();
            Change[] deferred = reverse.Where(Deferred).ToArray();
            bool SealedFile(Change change) => change.CreatedPath!.EndsWith(NoteStore.SensitiveExtension, StringComparison.OrdinalIgnoreCase);
            foreach (Change change in deferred.Where(change => !SealedFile(change)))
            {
                Finish(change, keepPublications);
            }
            CheckOriginals();
            bool keepSealed = incomplete;
            try
            {
                RefreshBinding();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                incomplete = true;
                // If the entire store was moved before retirement, all originals can
                // still be independently verified at their actual names in that store.
                keepSealed |= restoreNames.Count > 0;
            }
            foreach (Change change in deferred.Where(SealedFile))
            {
                Finish(change, keepSealed);
            }
            changes.Clear();
            if (incomplete)
            {
                throw new RecoveryFailure(recoveryLocations.Values.Where(CanReopen).Select(file => file.Path).Distinct(StringComparer.Ordinal).ToArray());
            }

            void CheckOriginals()
            {
                ResetOriginalEntries();
                originalRoot = true;
                foreach ((string path, Original kept) in originals)
                {
                    try
                    {
                        using SafeFileHandle parent = OpenEntry(Path.GetDirectoryName(path)!, true);
                        using SafeFileHandle file = OpenEntry(path, false);
                        RecoveryLocation original = NamedLocation(path, parent, Path.GetFileName(path), false, file,
                            kept.Expected.Hash, kept.Expected.Limit) ?? throw Ineligible();
                        if (recoveryLocations.TryGetValue(path, out RecoveryLocation? earlier) && earlier.Key != path)
                        {
                            recoveryLocations[earlier.Path] = earlier;
                        }
                        recoveryLocations[path] = original;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        incomplete = true;
                        if (recoveryLocations.TryGetValue(path, out RecoveryLocation? existing) && CanReopen(existing))
                        {
                            continue;
                        }
                        recoveryLocations.Remove(path);
                        if (!HasSealedFallback(path))
                        {
                            KeepRecoveryCopy(path, kept);
                            keepPublications |= !recoveryLocations.ContainsKey(path);
                        }
                    }
                }
                try
                {
                    VerifyOriginalHistory();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    incomplete = true;
                }
            }

            void Finish(Change change, bool keep)
            {
                try
                {
                    if (!keep)
                    {
                        change.Undo();
                    }
                    else if (change.Location?.Invoke() is { } location)
                    {
                        recoveryLocations[location.Path] = location;
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    incomplete = true;
                    keepPublications = true;
                    if (change.Location?.Invoke() is { } location)
                    {
                        recoveryLocations[location.Path] = location;
                    }
                }
                finally
                {
                    change.Release();
                }
            }

            bool HasSealedFallback(string path)
            {
                string sealedPath = path == originalPath ? Path.ChangeExtension(path, NoteStore.SensitiveExtension)
                    : Path.Combine(Key(NoteHistory.FolderFor(originalPath)), NoteHistory.FileName(Path.GetFileNameWithoutExtension(path), originalNote.Name));
                return reverse.FirstOrDefault(c => c.CreatedPath == sealedPath)?.Location?.Invoke() is not null;
            }
        }

        private void KeepRecoveryCopy(string path, Original kept)
        {
            // The version or attachment directory itself can be unavailable. The
            // independent store-root name uses the same durable per-note namespace.
            foreach (string parentPath in new[] { Path.GetDirectoryName(path)!, source.root }.Distinct(StringComparer.Ordinal))
            {
                try
                {
                    using SafeFileHandle parent = OpenEntry(parentPath, true);
                    using SafeFileHandle working = ReopenFolder(parent, shareDelete: false);
                    string leaf = RecoveryName(path);
                    string candidate = Path.Combine(parentPath, leaf);
                    CheckAvailable(candidate);
                    using CreatedFile copy = JournalDirectory.WriteNew(working, leaf, kept.Bytes, () => CheckAvailable(candidate));
                    if (NamedLocation(candidate, working, leaf, false, copy.Handle, kept.Expected.Hash, kept.Expected.Limit) is { } verified)
                    {
                        recoveryLocations[path] = verified;
                        return;
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Try the other directory while the captured bytes are still available.
                }
            }
        }

        private void ResetOriginalEntries()
        {
            entries.Clear();
            absent.Clear();
            foreach ((string path, Observation folder) in originalFolders.Concat(selection))
            {
                entries[path] = folder;
            }
            foreach ((string path, Original kept) in originals)
            {
                entries[path] = kept.Expected;
            }
            foreach ((string path, EntryIdentity restored) in restoredEntries)
            {
                if (entries.TryGetValue(path, out Observation? entry))
                {
                    entries[path] = entry with { Identity = restored };
                }
            }
            originalRoot = pinnedHistory = false;
        }

        private void RefreshBinding()
        {
            using SafeFileHandle current = OpenEntry(originalPath, false);
            if (entries[originalPath] is { Hash: { } hash, Limit: long limit } && CopyAndHash(current, limit) != hash)
            {
                throw Ineligible();
            }
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
                try
                {
                    using FileStream reopened = editor.OpenNote(originalNote);
                    if (entries[originalPath].Hash is { } digest && CopyAndHash(reopened.SafeFileHandle, entries[originalPath].Limit) != digest)
                    {
                        throw Ineligible();
                    }
                }
                catch
                {
                    foreach ((string path, Observation initial) in selection)
                    {
                        editor.observed[path] = initial.Identity;
                    }
                    throw;
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
