using TaskPane.Core.Validation;

namespace TaskPane.Core.Tests;

public sealed class SubtaskUiPolicyTests
{
    [Fact]
    public void Roots_may_offer_add_subtask()
    {
        Assert.True(SubtaskUiPolicy.CanOfferAddSubtask(isSubtask: false));
    }

    [Fact]
    public void Subtasks_must_not_offer_add_subtask()
    {
        Assert.False(SubtaskUiPolicy.CanOfferAddSubtask(isSubtask: true));
    }

    [Fact]
    public void Placeholder_title_is_stable_for_inline_rename()
    {
        Assert.Equal("New subtask", SubtaskUiPolicy.NewSubtaskPlaceholderTitle);
        Assert.True(SubtaskUiPolicy.SubtaskIndentDip > 0);
    }
}
