using AccessibilityAgent.Checks;
using Xunit;

namespace AccessibilityAgent.Tests.Checks;

public sealed class MetadataBuilderTests
{
    // Проверяет, что метод FromPairs отфильтровывает пары с пустыми значениями
    // (null, пустая строка, строка из пробелов) и возвращает словарь только с валидными парами.
    [Fact(DisplayName = "FromPairs filters out empty values")]
    public void FromPairs_FiltersEmptyEntries_ReturnsDictionary()
    {
        var result = MetadataBuilder.FromPairs(
            ("alpha", "one"),
            ("beta", string.Empty),
            ("gamma", null),
            ("delta", "   "),
            ("epsilon", "two")
        );

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("one", result["alpha"]);
        Assert.Equal("two", result["epsilon"]);
    }

    // Проверяет, что если после фильтрации не остаётся ни одной валидной пары,
    // метод возвращает null, а не пустой словарь.
    [Fact(DisplayName = "FromPairs returns null when nothing remains")]
    public void FromPairs_NoValidEntries_ReturnsNull()
    {
        var result = MetadataBuilder.FromPairs(
            ("alpha", null),
            (string.Empty, "value"),
            ("beta", " ")
        );

        Assert.Null(result);
    }

    // Проверяет, что ключи обрабатываются без учёта регистра и
    // более поздняя запись перезаписывает раннюю (case-insensitive override).
    [Fact(DisplayName = "FromPairs overwrites keys case-insensitively")]
    public void FromPairs_DuplicateKeys_OverridesCaseInsensitive()
    {
        var result = MetadataBuilder.FromPairs(
            ("Header", "first"),
            ("header", "second")
        );

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("second", result["header"]);
    }
}
