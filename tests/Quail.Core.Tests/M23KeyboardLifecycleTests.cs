using Quail.App;

namespace Quail.Core.Tests;

public sealed class M23KeyboardLifecycleTests
{
    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(false, false, 0)]
    public void Global_activation_targets_the_only_visible_search_surface(
        bool fullSearchVisible,
        bool fullSearchMinimized,
        int expected)
    {
        Assert.Equal((SearchSurfaceActivationTarget)expected, SearchSurfaceActivation.ForGlobalActivation(fullSearchVisible, fullSearchMinimized));
    }

    [Fact]
    public void Focus_request_requires_current_visible_window_and_actual_confirmation()
    {
        Assert.True(FullSearchLifecycle.ShouldApplyDeferredQueryFocus(true, true, false, 4, 4, 0));
        Assert.True(FullSearchLifecycle.ShouldRetryDeferredQueryFocus(true, true, false, 4, 4, 1, queryBoxOwnsKeyboardFocus: false));
        Assert.False(FullSearchLifecycle.ShouldRetryDeferredQueryFocus(true, true, false, 4, 4, 2, queryBoxOwnsKeyboardFocus: false));
        Assert.False(FullSearchLifecycle.ShouldApplyDeferredQueryFocus(true, true, false, 3, 4, 0));
        Assert.False(FullSearchLifecycle.ShouldApplyDeferredQueryFocus(true, false, false, 4, 4, 0));
        Assert.False(FullSearchLifecycle.ShouldApplyDeferredQueryFocus(true, true, true, 4, 4, 0));
        Assert.False(FullSearchLifecycle.ShouldCompleteDeferredQueryFocus(queryBoxOwnsKeyboardFocus: false));
        Assert.True(FullSearchLifecycle.ShouldCompleteDeferredQueryFocus(queryBoxOwnsKeyboardFocus: true));
    }

    [Fact]
    public void Shortcut_policy_routes_existing_actions_without_intercepting_query_copy()
    {
        Assert.Equal(SearchShortcutAction.SwitchMode, SearchShortcutPolicy.Resolve(true, false, false, true, false, false, false, false));
        Assert.Equal(SearchShortcutAction.Reveal, SearchShortcutPolicy.Resolve(true, false, true, false, false, true, true, false));
        Assert.Equal(SearchShortcutAction.None, SearchShortcutPolicy.Resolve(true, false, true, false, false, true, false, false));
        Assert.Equal(SearchShortcutAction.CopyPath, SearchShortcutPolicy.Resolve(false, true, true, false, true, true, false, true));
        Assert.Equal(SearchShortcutAction.None, SearchShortcutPolicy.Resolve(false, true, true, false, false, true, false, true));
        Assert.True(SearchShortcutPolicy.PreservesQueryTextCopy(queryBoxFocused: true, controlDown: true, shiftDown: false));
        Assert.False(SearchShortcutPolicy.PreservesQueryTextCopy(queryBoxFocused: true, controlDown: true, shiftDown: true));
    }
}
