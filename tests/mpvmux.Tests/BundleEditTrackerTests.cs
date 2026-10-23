using mpvmux.Models;
using mpvmux.Services;
using System;
using System.Collections.Generic;
using Xunit;

namespace mpvmux.Tests;

public sealed class BundleEditTrackerTests
{
    private readonly BundleSession _session = new();

    private BundleEditTracker CreateTracker(out List<string> events)
    {
        var tracker = new BundleEditTracker(_session);
        var log = new List<string>();

        tracker.Edited += (_, _) => log.Add("edited");
        tracker.DirtyChanged += (_, _) => log.Add($"dirty:{tracker.IsDirty}");

        events = log;
        return tracker;
    }

    [Fact]
    public void AFreshBundleIsClean()
    {
        var tracker = CreateTracker(out var events);

        Assert.False(tracker.IsDirty);
        Assert.Equal(0, tracker.Revision);
        Assert.Empty(events);
    }

    [Fact]
    public void EditingAnyListMarksTheBundleDirty()
    {
        var tracker = CreateTracker(out var events);

        _session.Bundle.VideoList.Add(new MediaRecord("episode-01", "/media/episode-01.mkv"));

        Assert.True(tracker.IsDirty);
        Assert.Equal(1, tracker.Revision);
        Assert.Equal(["dirty:True", "edited"], events);
    }

    [Fact]
    public void RemovingAndReplacingEntriesCountsAsEdits()
    {
        _session.Bundle.AudioList.Add(new MediaRecord("episode-01", "/media/episode-01.flac"));
        var tracker = CreateTracker(out _);

        _session.Bundle.AudioList[0] = new MediaRecord("episode-01", "/media/episode-01.m4a");
        _session.Bundle.AudioList.RemoveAt(0);
        _session.Bundle.SubtitleList.Add(new MediaRecord("episode-01", "/media/episode-01.srt"));

        Assert.Equal(3, tracker.Revision);
    }

    [Fact]
    public void TheSelectedRowAndVolumeAreEditsBecauseTheFileStoresThem()
    {
        var tracker = CreateTracker(out _);

        _session.Bundle.Volume = 42;
        _session.Bundle.SelectedVideoIndex = 3;

        Assert.Equal(2, tracker.Revision);
        Assert.True(tracker.IsDirty);
    }

    [Fact]
    public void MarkSavedClearsTheBundleWhenNothingChangedMeanwhile()
    {
        var tracker = CreateTracker(out var events);
        _session.Bundle.Volume = 42;

        var revision = tracker.Revision;
        tracker.MarkSaved(revision);

        Assert.False(tracker.IsDirty);
        Assert.Contains("dirty:False", events);
    }

    [Fact]
    public void MarkSavedKeepsTheBundleDirtyWhenAnEditArrivedDuringTheSave()
    {
        var tracker = CreateTracker(out _);

        var revision = tracker.Revision;
        _session.Bundle.Volume = 42;
        tracker.MarkSaved(revision);

        Assert.True(tracker.IsDirty);
    }

    [Fact]
    public void ASuspendedTrackerIgnoresEdits()
    {
        var tracker = CreateTracker(out var events);

        using (tracker.Suspend())
        {
            _session.Bundle.VideoList.Add(new MediaRecord("episode-01", "/media/episode-01.mkv"));
            _session.Bundle.Volume = 10;
        }

        Assert.False(tracker.IsDirty);
        Assert.Empty(events);

        // tracking resumes once the suspension ends
        _session.Bundle.Volume = 20;
        Assert.True(tracker.IsDirty);
    }

    [Fact]
    public void ASuspendedEditDoesNotHideALaterOne()
    {
        var tracker = CreateTracker(out _);
        _session.Bundle.Volume = 20;

        var revision = tracker.Revision;
        using (tracker.Suspend())
        {
            _session.Bundle.Volume = 30;
        }

        tracker.MarkSaved(revision);

        Assert.False(tracker.IsDirty);
    }

    [Fact]
    public void AReplacedListKeepsBeingTracked()
    {
        var tracker = CreateTracker(out _);

        _session.Bundle.VideoList = [new MediaRecord("episode-01", "/media/episode-01.mkv")];
        var revision = tracker.Revision;

        _session.Bundle.VideoList.Add(new MediaRecord("episode-02", "/media/episode-02.mkv"));

        Assert.Equal(revision + 1, tracker.Revision);
        Assert.True(tracker.IsDirty);
    }

    [Fact]
    public void SwappingTheBundleStartsCleanAndDetachesTheOldOne()
    {
        var tracker = CreateTracker(out var events);
        var old = _session.Bundle;
        _session.Bundle.VideoList.Add(new MediaRecord("episode-01", "/media/episode-01.mkv"));

        _session.Bundle = new MediaBundleModel();

        Assert.False(tracker.IsDirty);
        Assert.Contains("dirty:False", events);

        // edits to the detached bundle must not leak into the new document
        old.VideoList.Add(new MediaRecord("episode-02", "/media/episode-02.mkv"));
        Assert.False(tracker.IsDirty);

        _session.Bundle.AudioList.Add(new MediaRecord("episode-01", "/media/episode-01.flac"));
        Assert.True(tracker.IsDirty);
    }

    [Fact]
    public void ALoadedBundleWithReplacedCollectionsIsTracked()
    {
        var tracker = CreateTracker(out _);

        // deserialization replaces all three collections on the instance that is attached
        _session.Bundle.VideoList = [new MediaRecord("episode-01", "/media/episode-01.mkv")];
        tracker.MarkSaved(tracker.Revision);

        _session.Bundle.VideoList.Add(new MediaRecord("episode-02", "/media/episode-02.mkv"));

        Assert.True(tracker.IsDirty);
    }
}
