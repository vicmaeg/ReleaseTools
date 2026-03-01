#!/usr/bin/env dotnet
#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable
#:property PublishAot=false
#:package CliWrap@3.10.0
#:package Spectre.Console.Cli@0.53.0
#:project ../shared/ReleaseTools.Shared.csproj

using System.ComponentModel;
using System.Text.Json;
using ReleaseTools.Shared;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.AddCommand<NextCommand>("next")
        .WithDescription("Calculate the next SemVer version without creating a tag")
        .WithExample(new[] { "next" });
    config.AddCommand<TagCommand>("tag")
        .WithDescription("Create a git tag with the next SemVer version")
        .WithExample(new[] { "tag" });
});

return await app.RunAsync(args);

public class NextCommand : AsyncCommand<NextCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-p|--prefix")]
        [Description("Tag prefix for monorepo scenarios")]
        public string? Prefix { get; init; }

        [CommandOption("-f|--folder")]
        [Description("Filter commits to a specific folder path")]
        public string? Folder { get; init; }

        [CommandOption("-o|--output")]
        [Description("Output format: text or json")]
        [DefaultValue("text")]
        public string Output { get; init; } = "text";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            const string schema = "{MAJOR}.{MINOR}.{PATCH}";
            var calculator = new VersionCalculator();
            var result = await calculator.CalculateNextVersionAsync(schema, settings.Prefix, settings.Folder);

            if (settings.Output.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                var json = JsonSerializer.Serialize(new
                {
                    result.Version,
                    result.Mode,
                    result.BaseTag,
                    CommitsSinceTag = result.CommitsSinceTag,
                    Increment = result.Increment.ToString(),
                    result.IncrementReason,
                    result.Schema
                }, new JsonSerializerOptions { WriteIndented = true });
                Console.Write(json);
            }
            else
            {
                AnsiConsole.Write(result.Version);
            }
            return 0;
        }
        catch (SchemaMismatchException ex)
        {
            AnsiConsole.MarkupLine("[red]Error: Schema mode mismatch[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine(ex.Message);
            return 4;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
}

public class TagCommand : AsyncCommand<TagCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-p|--prefix")]
        [Description("Tag prefix for monorepo scenarios")]
        public string? Prefix { get; init; }

        [CommandOption("-f|--folder")]
        [Description("Filter commits to a specific folder path")]
        public string? Folder { get; init; }

        [CommandOption("-m|--message")]
        [Description("Tag message")]
        public string? Message { get; init; }

        [CommandOption("-a|--annotate")]
        [Description("Create an annotated tag")]
        [DefaultValue(false)]
        public bool Annotated { get; init; }

        [CommandOption("--push")]
        [Description("Push tag to origin after creation")]
        [DefaultValue(false)]
        public bool Push { get; init; }

        [CommandOption("-o|--output")]
        [Description("Output format: text or json")]
        [DefaultValue("text")]
        public string Output { get; init; } = "text";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            const string schema = "{MAJOR}.{MINOR}.{PATCH}";
            var calculator = new VersionCalculator();
            var result = await calculator.CalculateNextVersionAsync(schema, settings.Prefix, settings.Folder);

            var gitService = new GitService();
            var tagName = settings.Prefix != null ? $"{settings.Prefix}{result.Version}" : result.Version;

            gitService.CreateTagAsync(tagName, settings.Message, settings.Annotated).GetAwaiter().GetResult();

            if (settings.Push)
            {
                gitService.PushTagAsync(tagName).GetAwaiter().GetResult();
            }

            if (settings.Output.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                var json = JsonSerializer.Serialize(new
                {
                    Version = result.Version,
                    TagName = tagName,
                    result.Mode,
                    Annotated = settings.Annotated,
                    Pushed = settings.Push,
                    result.Schema
                }, new JsonSerializerOptions { WriteIndented = true });
                Console.Write(json);
            }
            else
            {
                AnsiConsole.WriteLine($"Created tag: {tagName}");
                if (settings.Push)
                {
                    AnsiConsole.WriteLine("Pushed to origin");
                }
            }
            return 0;
        }
        catch (SchemaMismatchException ex)
        {
            AnsiConsole.MarkupLine("[red]Error: Schema mode mismatch[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine(ex.Message);
            return 4;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }
}
