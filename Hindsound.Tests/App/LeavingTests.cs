using Hindsound.App;

namespace Hindsound.Tests.App;

public class SessionEndBlockTests
{
    [Fact]
    public void No_clip_means_no_reason_to_give()
    {
        var block = new SessionEndBlock();

        Assert.False(block.Asked(clipsBusy: false));
        Assert.False(block.Over());
    }

    [Fact]
    public void A_clip_being_written_gives_the_reason_once_and_takes_it_back_once()
    {
        var block = new SessionEndBlock();

        Assert.True(block.Asked(clipsBusy: true));
        Assert.False(block.Asked(clipsBusy: true));
        Assert.True(block.Over());
        Assert.False(block.Over());
    }

    [Fact]
    public void A_session_that_went_on_can_end_later_with_a_new_reason()
    {
        var block = new SessionEndBlock();
        block.Asked(clipsBusy: true);
        block.Over();

        Assert.True(block.Asked(clipsBusy: true));
    }

    [Fact]
    public void The_words_and_the_limit_are_the_ones_decided()
    {
        Assert.Equal("Saving your clip", SessionEndBlock.Reason);
        Assert.Equal(TimeSpan.FromSeconds(15), SessionEndBlock.ClipsLimit);
    }
}
