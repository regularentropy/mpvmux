using mpvmux.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace mpvmux.Services;

/// <summary>
/// Tracks whether the open bundle still matches what was last written to disk. Everything the
/// .mpvm file stores counts as an edit — the three track lists, the selected row and the volume —
/// because autosave writes all of it. All members are expected to be used on the UI thread.
/// </summary>
internal interface IBundleEditTracker
{
    /// <summary>Raised once per tracked edit while tracking is not suspended.</summary>
    event EventHandler? Edited;

    /// <summary>Raised whenever <see cref="IsDirty"/> changes, including on a bundle swap.</summary>
    event EventHandler? DirtyChanged;

    bool IsDirty { get; }

    /// <summary>
    /// Increases with every tracked edit. A save reads this before writing so it can tell
    /// whether an edit arrived while the file was being written.
    /// </summary>
    int Revision { get; }

    /// <summary>
    /// Ignores edits until disposed. Used for writes that change the bundle without being user
    /// edits, mainly the fingerprint capture inside a save.
    /// </summary>
    IDisposable Suspend();

    /// <summary>
    /// Marks the bundle clean unless it was edited after <paramref name="revision"/> was read.
    /// </summary>
    void MarkSaved(int revision);
}

internal sealed class BundleEditTracker : IBundleEditTracker
{
    private readonly BundleSession _session;
    private readonly List<ObservableCollection<MediaRecord>> _attachedLists = [];
    private MediaBundleModel _bundle;
    private int _suspendDepth;
    private bool _isDirty;

    public BundleEditTracker(BundleSession session)
    {
        _session = session;
        _bundle = session.Bundle;

        Attach(_bundle);
        session.PropertyChanged += OnSessionBundleChanged;
    }

    public event EventHandler? Edited;

    public event EventHandler? DirtyChanged;

    public bool IsDirty => _isDirty;

    public int Revision { get; private set; }

    public IDisposable Suspend()
    {
        _suspendDepth++;
        return new Suspension(this);
    }

    public void MarkSaved(int revision)
    {
        if (revision == Revision)
            SetDirty(false);
    }

    private void OnSessionBundleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BundleSession.Bundle))
            return;

        Detach();
        _bundle = _session.Bundle;
        Attach(_bundle);

        // a bundle that was just loaded or created matches its file
        SetDirty(false);
    }

    private void OnBundlePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MediaBundleModel.VideoList):
            case nameof(MediaBundleModel.AudioList):
            case nameof(MediaBundleModel.SubtitleList):
                // Deserialization replaces the collection instance, which kills the old
                // subscription; re-attach to whatever the bundle now holds.
                AttachLists();
                RecordEdit();
                break;
            case nameof(MediaBundleModel.SelectedVideoIndex):
            case nameof(MediaBundleModel.Volume):
                RecordEdit();
                break;
            // SchemaVersion is written by the serializer only, never by an edit
        }
    }

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e) => RecordEdit();

    private void RecordEdit()
    {
        if (_suspendDepth > 0)
            return;

        Revision++;
        SetDirty(true);
        Edited?.Invoke(this, EventArgs.Empty);
    }

    private void Attach(MediaBundleModel bundle)
    {
        bundle.PropertyChanged += OnBundlePropertyChanged;
        AttachLists();
    }

    private void AttachLists()
    {
        DetachLists();

        foreach (var list in new[] { _bundle.VideoList, _bundle.AudioList, _bundle.SubtitleList })
        {
            if (list is null)
                continue;

            list.CollectionChanged += OnListChanged;
            _attachedLists.Add(list);
        }
    }

    private void Detach()
    {
        _bundle.PropertyChanged -= OnBundlePropertyChanged;
        DetachLists();
    }

    private void DetachLists()
    {
        foreach (var list in _attachedLists)
            list.CollectionChanged -= OnListChanged;

        _attachedLists.Clear();
    }

    private void SetDirty(bool value)
    {
        if (_isDirty == value)
            return;

        _isDirty = value;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Suspension(BundleEditTracker tracker) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            tracker._suspendDepth--;
        }
    }
}
