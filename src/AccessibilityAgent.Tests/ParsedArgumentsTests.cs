using System;
using AccessibilityAgent.Cli;
using Xunit;

namespace AccessibilityAgent.Tests;

public sealed class ParsedArgumentsTests
{
    // Проверяем, что парсер понимает длинные ключи в форме --ключ=значение.
    [Fact(DisplayName = "Parse accepts --key=value syntax")]
    public void Parse_LongOptionWithEquals_ReturnsValue()
    {
        var parsed = ParsedArguments.Parse(["--output=result.txt"]);

        Assert.Equal("result.txt", parsed.GetOption(["output"]));
        Assert.Empty(parsed.Positionals);
    }

    // Проверяем, что значение длинной опции берётся из следующего токена.
    [Fact(DisplayName = "Parse consumes value after --key token")]
    public void Parse_LongOptionWithSeparatedValue_ConsumesNextToken()
    {
        var parsed = ParsedArguments.Parse(["--output", "result.txt", "extra"]);

        Assert.Equal("result.txt", parsed.GetOption(["output"]));
        Assert.Equal(["extra"], parsed.Positionals);
    }

    // Проверяем, что короткий флаг без значения считается true.
    [Fact(DisplayName = "Parse treats short flag without value as true")]
    public void Parse_ShortFlagWithoutValue_SetsFlag()
    {
        var parsed = ParsedArguments.Parse(["-v"]);

        Assert.True(parsed.HasFlag(["verbose", "v"]));
        Assert.False(parsed.IsHelpRequested);
    }

    // Проверяем, что короткая опция использует значение из следующего аргумента.
    [Fact(DisplayName = "Parse consumes value after short option token")]
    public void Parse_ShortOptionWithValue_ConsumesNextToken()
    {
        var parsed = ParsedArguments.Parse(["-o", "file.txt"]);

        Assert.Equal("file.txt", parsed.GetOption(["output", "o"]));
    }

    // Проверяем, что длинный флаг без значения считается true.
    [Fact(DisplayName = "Parse treats long flag without value as true")]
    public void Parse_LongFlagWithoutValue_DefaultsToTrue()
    {
        var parsed = ParsedArguments.Parse(["--force"]);

        Assert.True(parsed.HasFlag(["force"]));
    }

    // Проверяем, что значения собираются для всех алиасов без учёта регистра.
    [Fact(DisplayName = "GetOptionValues aggregates aliases case-insensitively")]
    public void GetOptionValues_CollectsAcrossAliases()
    {
        var parsed = ParsedArguments.Parse(["--tag=alpha", "--TAG=beta", "-t", "gamma"]);

        Assert.Equal(["alpha", "beta", "gamma"], parsed.GetOptionValues(["tag", "t"]));
    }

    // Проверяем, что для первого совпавшего алиаса возвращается последнее значение.
    [Fact(DisplayName = "GetOption returns last value for the first matching alias")]
    public void GetOption_ReturnsLastValueWhenRepeated()
    {
        var parsed = ParsedArguments.Parse(["--output=first", "-o", "second"]);

        Assert.Equal("first", parsed.GetOption(["output", "o"]));
        Assert.Equal("second", parsed.GetOption(["o", "output"]));
        Assert.Equal(["first", "second"], parsed.GetOptionValues(["output", "o"]));
    }

    // Проверяем, что числовая опция читается и сравнивается с границами.
    [Fact(DisplayName = "TryGetIntOption parses bounded integer value")]
    public void TryGetIntOption_ReturnsParsedValue()
    {
        var parsed = ParsedArguments.Parse(["--count=15"]);

        Assert.Equal(15, parsed.TryGetIntOption(["count"], minValue: 5, maxValue: 20));
    }

    // Проверяем, что нарушение нижней границы приводит к исключению.
    [Fact(DisplayName = "TryGetIntOption enforces minimum bound")]
    public void TryGetIntOption_ValueBelowMin_Throws()
    {
        var parsed = ParsedArguments.Parse(["--count=1"]);

        var ex = Assert.Throws<ArgumentException>(() => parsed.TryGetIntOption(["count"], minValue: 5));
        Assert.Contains("must be >=", ex.Message);
    }

    // Проверяем, что нарушение верхней границы приводит к исключению.
    [Fact(DisplayName = "TryGetIntOption enforces maximum bound")]
    public void TryGetIntOption_ValueAboveMax_Throws()
    {
        var parsed = ParsedArguments.Parse(["--count=50"]);

        var ex = Assert.Throws<ArgumentException>(() => parsed.TryGetIntOption(["count"], maxValue: 40));
        Assert.Contains("must be <=", ex.Message);
    }

    // Проверяем, что нечисловое значение приводит к исключению.
    [Fact(DisplayName = "TryGetIntOption rejects non-numeric input")]
    public void TryGetIntOption_InvalidValue_Throws()
    {
        var parsed = ParsedArguments.Parse(["--count=abc"]);

        var ex = Assert.Throws<ArgumentException>(() => parsed.TryGetIntOption(["count"]));
        Assert.Contains("Invalid integer value", ex.Message);
    }

    // Проверяем fallback на позиционный аргумент при отсутствии опции.
    [Fact(DisplayName = "RequireIntOption falls back to positional argument")]
    public void RequireIntOption_FallsBackToPositional()
    {
        var parsed = ParsedArguments.Parse(["42", "remaining"]);

        Assert.Equal(42, parsed.RequireIntOption(["count"], minValue: 1, maxValue: 100));
        Assert.Equal(["remaining"], parsed.Positionals);
    }

    // Проверяем, что отсутствие числового значения приводит к исключению.
    [Fact(DisplayName = "RequireIntOption reports missing argument")]
    public void RequireIntOption_MissingValue_Throws()
    {
        var parsed = ParsedArguments.Parse(Array.Empty<string>());

        var ex = Assert.Throws<ArgumentException>(() => parsed.RequireIntOption(["count"]));
        Assert.Contains("Missing required option", ex.Message);
    }

    // Проверяем, что некорректный позиционный аргумент приводит к исключению.
    [Fact(DisplayName = "RequireIntOption validates positional parsing errors")]
    public void RequireIntOption_PositionalInvalidValue_Throws()
    {
        var parsed = ParsedArguments.Parse(["invalid"]);

        var ex = Assert.Throws<ArgumentException>(() => parsed.RequireIntOption(["count"]));
        Assert.Contains("Invalid integer value", ex.Message);
    }

    // Проверяем, что позиционные аргументы выдаются последовательно и исчезают.
    [Fact(DisplayName = "ConsumePositional shifts positionals one by one")]
    public void ConsumePositional_RemovesItemsSequentially()
    {
        var parsed = ParsedArguments.Parse(["first", "second"]);

        Assert.Equal("first", parsed.ConsumePositional());
        Assert.Equal("second", parsed.ConsumePositional());
        Assert.Null(parsed.ConsumePositional());
    }

    // Проверяем, что хотя бы одно true среди значений флага даёт положительный результат.
    [Fact(DisplayName = "HasFlag returns true when any alias has true value")]
    public void HasFlag_OnlyTrueValuesCount()
    {
        var parsed = ParsedArguments.Parse(["--skip=false", "--skip=true"]);

        Assert.True(parsed.HasFlag(["skip"]));
    }
}
