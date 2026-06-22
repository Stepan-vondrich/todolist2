using TodoApi.Models;

namespace TodoApi.Tests;

public class TodoItemModelTests
{
    [Fact]
    public void TodoItem_DefaultStatus_IsEmpty()
    {
        var item = new TodoItem();
        Assert.Equal("", item.Status);
    }

    [Fact]
    public void TodoItem_DefaultIsCompleted_IsFalse()
    {
        var item = new TodoItem();
        Assert.False(item.IsCompleted);
    }
}
