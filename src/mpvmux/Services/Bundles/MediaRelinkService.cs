using mpvmux.Constants;
using mpvmux.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace mpvmux.Services;

/// <summary>The three aligned columns a media reference can live in.</summary>
internal enum MediaSlot
{
    Video,
    Audio,
    Subtitle
}

/// <summary>Why a candidate was considered a plausible new home for a missing file.</summary>
internal enum MediaRelinkReason
{
    /// <summary>Same file name somewhere else under the original folder.</summary>
    ExactName,

    /// <summary>Same path, differing only in casing.</summary>
    CaseOnly,

    /// <summary>Identical sampled content, which survives a rename even of the extension.</summary>
    ContentHash
}

/// <summary>One file that could be the renamed version of a missing reference.</summary>
internal sealed record MediaRelinkCandidate(string Path, MediaRelinkReason Reason);

/// <summary>
/// A missing track cell and the files that could replace it, best candidate first. Proposals are
/// suggestions only; <see cref="IMediaRelinkService.Apply"/> is what changes a bundle.
/// </summary>
internal sealed record MediaRelinkProposal(
    MediaSlot Slot,
    int Index,
    MediaRecord Original,
    IReadOnlyList<MediaRelinkCandidate> Candidates)
{
    /// <summary>How many alternatives the review dialog offers for one cell.</summary>
    public const int MaxCandidates = 5;

    public MediaRelinkCandidate? Best => Candidates.Count > 0 ? Candidates[0] : null;

    public string SlotLabel => Slot switch
    {
        MediaSlot.Video => "video",
        MediaSlot.Audio => "audio",
        _ => "subtitle"
    };

    /// <summary>Keeps the ranked candidates bounded; anything past the fifth is dropped.</summary>
    public static MediaRelinkProposal Create(
        MediaSlot slot,
        int index,
        MediaRecord original,
        IEnumerable<MediaRelinkCandidate> candidates) =>
        new(slot, index, original, candidates.Take(MaxCandidates).ToList());
}

/// <summary>A confirmed replacement, captured together with the path it was detected for.</summary>
internal sealed record MediaRelinkSelection(MediaSlot Slot, int Index, string OriginalPath, string NewPath);

/// <summary>
/// Finds media files that are referenced by a bundle but no longer present, and proposes the
/// files they were most likely renamed to. Detection is read-only: nothing is rewritten until the
/// user confirms a proposal.
/// </summary>
internal interface IMediaRelinkService
{
    /// <summary>
    /// Scans the folders referenced by <paramref name="bundle"/> for replacements of missing
    /// files. Unreadable folders and invalid paths yield no candidates instead of failing, so a
    /// scan can always run against an arbitrary bundle; cancellation is the only exception.
    /// </summary>
    Task<IReadOnlyList<MediaRelinkProposal>> DetectAsync(
        MediaBundleModel bundle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rewrites the confirmed selections in place. Selections whose cell changed since detection
    /// are skipped, and the number of applied replacements is returned.
    /// </summary>
    int Apply(MediaBundleModel bundle, IReadOnlyCollection<MediaRelinkSelection> selections);
}

internal sealed class MediaRelinkService : IMediaRelinkService
{
    public Task<IReadOnlyList<MediaRelinkProposal>> DetectAsync(
        MediaBundleModel bundle,
        CancellationToken cancellationToken = default)
    {
        // folder walks are blocking, and a large tree is exactly where a UI thread must not be
        return Task.Run(() => Detect(bundle, cancellationToken), cancellationToken);
    }

    public int Apply(MediaBundleModel bundle, IReadOnlyCollection<MediaRelinkSelection> selections)
    {
        var applied = 0;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var selection in selections)
        {
            if (string.IsNullOrWhiteSpace(selection.NewPath))
                continue;

            var list = ListFor(bundle, selection.Slot);
            if (selection.Index < 0 || selection.Index >= list.Count)
                continue;

            var current = list[selection.Index];
            if (current is null)
                continue;

            // the cell was edited or cleared while the dialog was open; the user's edit wins
            if (!string.Equals(current.Path, selection.OriginalPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(current.Path, selection.NewPath, StringComparison.Ordinal))
                continue;

            // a file may be confirmed for one cell only, even if the dialog was open twice
            if (!used.Add(selection.NewPath))
                continue;

            if (!File.Exists(selection.NewPath))
                continue;

            var info = FingerprintOf(selection.NewPath);
            list[selection.Index] = current with
            {
                Name = Path.GetFileNameWithoutExtension(selection.NewPath),
                Path = selection.NewPath,
                SizeBytes = info?.Length,
                ModifiedUtc = info?.LastWriteTimeUtc
            };

            applied++;
        }

        return applied;
    }

    private static ObservableCollection<MediaRecord> ListFor(MediaBundleModel bundle, MediaSlot slot) => slot switch
    {
        MediaSlot.Video => bundle.VideoList,
        MediaSlot.Audio => bundle.AudioList,
        _ => bundle.SubtitleList
    };

    private static FileInfo? FingerprintOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static IReadOnlyList<MediaRelinkProposal> Detect(
        MediaBundleModel bundle,
        CancellationToken cancellationToken)
    {
        try
        {
            var missing = CollectMissing(bundle);
            if (missing.Count == 0)
                return [];

            // a file that another cell still points at must never be offered as a replacement
            var present = CollectPresentPaths(bundle);
            var scans = new Dictionary<string, FolderScan>(StringComparer.OrdinalIgnoreCase);

            var ranked = new List<RankedSlot>();
            foreach (var slot in missing)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var scan = GetScan(slot.Folder, scans, cancellationToken);
                var candidates = RankCandidates(slot, scan, present, cancellationToken);
                if (candidates.Count > 0)
                    ranked.Add(new RankedSlot(slot, candidates));
            }

            AssignOneToOne(ranked);

            // every missing cell is reported, including the ones no file matched: the grid needs
            // to flag them even when there is nothing to offer
            var bySlot = ranked.ToDictionary(
                item => (item.Slot.Slot, item.Slot.Index),
                item => item.Candidates.Select(candidate => candidate.Candidate));

            return missing
                .Select(slot => MediaRelinkProposal.Create(
                    slot.Slot,
                    slot.Index,
                    slot.Record,
                    bySlot.TryGetValue((slot.Slot, slot.Index), out var candidates)
                        ? candidates
                        : Enumerable.Empty<MediaRelinkCandidate>()))
                .ToList();
        }
        catch (IOException)
        {
            // a folder can disappear or be denied between enumeration and probing; a scan must
            // never take the grid down with it
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (ArgumentException)
        {
            // a hand-edited bundle can name a path this platform cannot parse
            return [];
        }
    }

    private static List<MissingSlot> CollectMissing(MediaBundleModel bundle)
    {
        var missing = new List<MissingSlot>();

        foreach (var (slot, list) in EnumerateLists(bundle))
        {
            for (var i = 0; i < list.Count; i++)
            {
                var record = list[i];
                if (record is null || string.IsNullOrWhiteSpace(record.Path))
                    continue;

                if (File.Exists(record.Path))
                    continue;

                var folder = FolderOf(record.Path);
                if (string.IsNullOrEmpty(folder))
                    continue;

                missing.Add(new MissingSlot(slot, i, record, folder));
            }
        }

        return missing;
    }

    private static HashSet<string> CollectPresentPaths(MediaBundleModel bundle)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, list) in EnumerateLists(bundle))
        {
            foreach (var record in list)
            {
                if (record is null || string.IsNullOrWhiteSpace(record.Path))
                    continue;

                if (File.Exists(record.Path))
                    present.Add(record.Path);
            }
        }

        return present;
    }

    private static IEnumerable<(MediaSlot Slot, ObservableCollection<MediaRecord> List)> EnumerateLists(
        MediaBundleModel bundle)
    {
        yield return (MediaSlot.Video, bundle.VideoList);
        yield return (MediaSlot.Audio, bundle.AudioList);
        yield return (MediaSlot.Subtitle, bundle.SubtitleList);
    }

    private static string? FolderOf(string path)
    {
        try
        {
            return Path.GetDirectoryName(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static FolderScan GetScan(
        string folder,
        Dictionary<string, FolderScan> scans,
        CancellationToken cancellationToken)
    {
        if (scans.TryGetValue(folder, out var cached))
            return cached;

        var scan = FolderScan.Read(folder, cancellationToken);
        scans[folder] = scan;
        return scan;
    }

    /// <summary>
    /// One folder of the bundle, read twice: the top level drives the conservative matches, while
    /// a recursive pass only serves exact-name lookups for files moved into a subfolder.
    /// </summary>
    private sealed class FolderScan
    {
        public IReadOnlyList<FileEntry> TopLevel { get; private init; } = [];

        public IReadOnlyList<FileEntry> Recursive { get; private init; } = [];

        public static FolderScan Read(string folder, CancellationToken cancellationToken)
        {
            var directory = new DirectoryInfo(folder);
            return new FolderScan
            {
                TopLevel = ReadFiles(directory, recurse: false, cancellationToken),
                Recursive = ReadFiles(directory, recurse: true, cancellationToken)
            };
        }

        private static List<FileEntry> ReadFiles(
            DirectoryInfo directory,
            bool recurse,
            CancellationToken cancellationToken)
        {
            var entries = new List<FileEntry>();

            try
            {
                if (!directory.Exists)
                    return entries;

                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = recurse,
                    IgnoreInaccessible = true,
                    // symlinks and junctions can create cycles and duplicate hits
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    ReturnSpecialDirectories = false
                };

                foreach (var file in directory.EnumerateFiles("*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        entries.Add(new FileEntry(file.FullName, file.Length, file.LastWriteTimeUtc));
                    }
                    catch (IOException)
                    {
                        // the file vanished while the folder was being walked
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return entries;
        }
    }

    private sealed class FileEntry(string path, long length, DateTime modifiedUtc)
    {
        private string? _contentHash;
        private bool _contentHashRead;

        public string Path { get; } = path;

        public long Length { get; } = length;

        public DateTime ModifiedUtc { get; } = modifiedUtc;

        /// <summary>
        /// Sampled content hash, computed on first use and cached for the rest of the scan. Only
        /// candidates whose length matches the missing record are ever asked for it, so a folder
        /// of normal files costs nothing extra.
        /// </summary>
        public string? ContentHash
        {
            get
            {
                if (!_contentHashRead)
                {
                    _contentHash = MediaFileFingerprint.ComputeContentHash(Path);
                    _contentHashRead = true;
                }

                return _contentHash;
            }
        }
    }

    private sealed record MissingSlot(MediaSlot Slot, int Index, MediaRecord Record, string Folder)
    {
        public string FileName => Path.GetFileName(Record.Path);
    }

    private sealed record ScoredCandidate(MediaRelinkCandidate Candidate, double Score);

    private sealed class RankedSlot(MissingSlot slot, List<ScoredCandidate> candidates)
    {
        public MissingSlot Slot { get; } = slot;

        public List<ScoredCandidate> Candidates { get; set; } = candidates;
    }

    private static List<ScoredCandidate> RankCandidates(
        MissingSlot slot,
        FolderScan scan,
        HashSet<string> present,
        CancellationToken cancellationToken)
    {
        var allowed = AllowedExtensions(slot.Slot);

        var scored = new List<ScoredCandidate>();

        // everything in the folder itself is fair game for every reason
        foreach (var entry in scan.TopLevel)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!allowed.Contains(Path.GetExtension(entry.Path)))
                continue;

            if (present.Contains(entry.Path) || PathsEqual(entry.Path, slot.Record.Path))
                continue;

            if (Score(entry, slot) is { } candidate)
                scored.Add(candidate);
        }

        // a file moved into a subfolder only counts when its name is unchanged
        foreach (var entry in scan.Recursive)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = Path.GetFileName(entry.Path);
            if (!string.Equals(name, slot.FileName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (present.Contains(entry.Path) || PathsEqual(entry.Path, slot.Record.Path))
                continue;

            var reason = PathsEqual(entry.Path, slot.Record.Path, ignoreCase: true)
                ? MediaRelinkReason.CaseOnly
                : MediaRelinkReason.ExactName;

            scored.Add(new ScoredCandidate(
                new MediaRelinkCandidate(entry.Path, reason),
                ScoreFor(reason)));
        }

        // one path can surface from both passes and from more than one reason
        var ranked = scored
            .GroupBy(item => item.Candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.MaxBy(item => item.Score)!)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Candidate.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // A definitive identification makes the softer guesses noise: the same path (case) or
        // the same bytes. Only bundles saved with a content hash can reach the second band, so
        // for older bundles nothing is definitive and every fallback stays in play.
        var definitive = ranked
            .Where(item => item.Candidate.Reason == MediaRelinkReason.CaseOnly)
            .ToList();

        if (definitive.Count == 0)
        {
            definitive = ranked
                .Where(item => item.Candidate.Reason == MediaRelinkReason.ContentHash)
                .ToList();
        }

        return definitive.Count > 0 ? definitive : ranked;
    }

    /// <summary>
    /// Scores a top-level file against one missing slot. <c>null</c> means the file is not a
    /// plausible replacement at all.
    /// </summary>
    private static ScoredCandidate? Score(FileEntry entry, MissingSlot slot)
    {
        var record = slot.Record;

        if (PathsEqual(entry.Path, record.Path, ignoreCase: true))
            return Wrap(entry, MediaRelinkReason.CaseOnly);

        // identical content is the most definitive evidence available: it survives a rename of
        // the name, the folder, the extension and the timestamps. The length is part of the hash,
        // so the equality check below is also the cheap prefilter that avoids reading files.
        if (record.ContentHash is { } expectedHash
            && record.SizeBytes == entry.Length
            && string.Equals(entry.ContentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            return Wrap(entry, MediaRelinkReason.ContentHash);
        }

        // an unchanged name is the only signal left for files whose content was re-encoded
        return string.Equals(Path.GetFileName(entry.Path), slot.FileName, StringComparison.OrdinalIgnoreCase)
            ? Wrap(entry, MediaRelinkReason.ExactName)
            : null;
    }

    private static ScoredCandidate Wrap(FileEntry entry, MediaRelinkReason reason) =>
        new(new MediaRelinkCandidate(entry.Path, reason), ScoreFor(reason));

    private static double ScoreFor(MediaRelinkReason reason) => reason switch
    {
        MediaRelinkReason.CaseOnly => 1000,
        MediaRelinkReason.ContentHash => 950,
        _ => 900
    };

    private static HashSet<string> AllowedExtensions(MediaSlot slot)
    {
        var extensions = slot switch
        {
            MediaSlot.Video => AppConstants.VideoExtensions,
            MediaSlot.Audio => AppConstants.AudioExtensions,
            _ => AppConstants.SubtitleExtensions
        };

        return new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves competing slots: a file that is the best match for one cell is not offered for
    /// another, while every slot keeps its own alternatives for the user to choose from.
    /// </summary>
    private static void AssignOneToOne(List<RankedSlot> slots)
    {
        var assigned = new Dictionary<string, MissingSlot>(StringComparer.OrdinalIgnoreCase);

        foreach (var slot in slots.OrderByDescending(slot => slot.Candidates[0].Score))
        {
            var winner = slot.Candidates.FirstOrDefault(candidate => !assigned.ContainsKey(candidate.Candidate.Path));
            if (winner is not null)
                assigned[winner.Candidate.Path] = slot.Slot;
        }

        foreach (var slot in slots)
        {
            slot.Candidates = slot.Candidates
                .Where(candidate =>
                    !assigned.TryGetValue(candidate.Candidate.Path, out var owner) || owner == slot.Slot)
                .ToList();
        }
    }

    private static bool PathsEqual(string left, string right, bool ignoreCase = false) =>
        string.Equals(
            left,
            right,
            ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
