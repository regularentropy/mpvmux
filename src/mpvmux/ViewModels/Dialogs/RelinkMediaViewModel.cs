using CommunityToolkit.Mvvm.ComponentModel;
using mpvmux.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace mpvmux.ViewModels;

/// <summary>One selectable replacement in the review dialog.</summary>
internal sealed record RelinkCandidateOption(string Path, string FileName, MediaRelinkReason Reason)
{
    public string ReasonLabel => Reason switch
    {
        MediaRelinkReason.CaseOnly => "Same file, different case",
        MediaRelinkReason.ContentHash => "Identical content",
        _ => "Same file name"
    };
}

/// <summary>One missing cell, its original file, and the replacements found for it.</summary>
internal sealed partial class RelinkMediaRowViewModel : ViewModelBase
{
    public RelinkMediaRowViewModel(MediaRelinkProposal proposal)
    {
        Proposal = proposal;
        OriginalFileName = Path.GetFileName(proposal.Original.Path);
        RowLabel = $"Row {proposal.Index + 1} · {proposal.SlotLabel}";

        Options = new ObservableCollection<RelinkCandidateOption>(
            proposal.Candidates.Select(candidate => new RelinkCandidateOption(
                candidate.Path,
                Path.GetFileName(candidate.Path),
                candidate.Reason)));

        // every proposed match starts included: the dialog exists so the user can reject one,
        // not so they have to opt in one by one
        _isIncluded = Options.Count > 0;
        _selectedOption = Options.FirstOrDefault();
    }

    public MediaRelinkProposal Proposal { get; }

    public string RowLabel { get; }

    public string OriginalFileName { get; }

    public string OriginalPath => Proposal.Original.Path;

    public bool HasCandidates => Options.Count > 0;

    public ObservableCollection<RelinkCandidateOption> Options { get; }

    [ObservableProperty]
    private bool _isIncluded;

    [ObservableProperty]
    private RelinkCandidateOption? _selectedOption;

    public MediaRelinkSelection? ToSelection() =>
        IsIncluded && SelectedOption is not null
            ? new MediaRelinkSelection(Proposal.Slot, Proposal.Index, OriginalPath, SelectedOption.Path)
            : null;
}

/// <summary>
/// Backs the review window: one row per missing cell with a checkbox and the ranked candidates.
/// Nothing is written until the window returns the confirmed selections.
/// </summary>
internal sealed partial class RelinkMediaViewModel : ViewModelBase
{
    public ObservableCollection<RelinkMediaRowViewModel> Rows { get; } = [];

    public void Initialize(IReadOnlyList<MediaRelinkProposal> proposals)
    {
        Rows.Clear();

        // only cells with something to offer are worth a row; the rest stay in the banner
        foreach (var proposal in proposals
                     .Where(proposal => proposal.Candidates.Count > 0)
                     .OrderBy(proposal => proposal.Index)
                     .ThenBy(proposal => proposal.Slot))
        {
            Rows.Add(new RelinkMediaRowViewModel(proposal));
        }
    }

    public IReadOnlyList<MediaRelinkSelection> BuildSelections() =>
        Rows.Select(row => row.ToSelection())
            .OfType<MediaRelinkSelection>()
            .ToList();
}
