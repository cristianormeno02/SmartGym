using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Services;

public partial class NotificationTemplateRenderer : INotificationTemplateRenderer
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex AnyBracesRegex = new(@"\{\{([^{}]*)\}\}", RegexOptions.Compiled);
    private static readonly Regex UnsafeMarkupRegex = new(
        @"<\s*\/?\s*(script|iframe|object|embed|applet|meta|link)\b|(?:\bon[a-z]+\s*=)|(?:javascript\s*:)|(?:vbscript\s*:)|(?:data\s*:\s*text\/html)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HtmlTagsRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    private readonly ISmartGymDbContext _dbContext;

    public NotificationTemplateRenderer(ISmartGymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<RenderedNotificationContent> RenderAsync(
        NotificationType type,
        NotificationChannel channel,
        IReadOnlyDictionary<string, string?> variables,
        string locale = "es-AR",
        CancellationToken cancellationToken = default)
    {
        var normalizedLocale = string.IsNullOrWhiteSpace(locale) ? "es-AR" : locale.Trim();

        var template = await _dbContext.NotificationTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(
                t => t.Type == type &&
                     t.Channel == channel &&
                     t.Locale == normalizedLocale &&
                     t.Status == NotificationTemplateStatus.Active &&
                     t.IsActive,
                cancellationToken);

        template ??= DefaultNotificationTemplates.GetFallback(type, channel, normalizedLocale);

        return RenderTemplate(template, variables);
    }

    public RenderedNotificationContent RenderTemplate(
        NotificationTemplate template,
        IReadOnlyDictionary<string, string?> variables)
    {
        var allowedSet = template.GetAllowedVariableSet();
        var lookup = new Dictionary<string, string?>(variables, StringComparer.Ordinal);

        var subjectRaw = template.SubjectTemplate ?? DefaultNotificationTemplates.GetTemplateDefinition(template.Type).Subject;
        var renderedSubject = ReplaceVariables(subjectRaw, lookup, allowedSet, htmlEncodeValues: false).Trim();
        var missingVariables = FindMissingVariables($"{subjectRaw} {template.BodyTemplate}", lookup, allowedSet);

        if (template.Channel == NotificationChannel.Email)
        {
            var renderedHtmlBody = ReplaceVariables(template.BodyTemplate, lookup, allowedSet, htmlEncodeValues: true).Trim();
            var plainSource = StripHtmlTags(template.BodyTemplate);
            var renderedPlainBody = ReplaceVariables(plainSource, lookup, allowedSet, htmlEncodeValues: false).Trim();

            return new RenderedNotificationContent(
                renderedSubject,
                renderedHtmlBody,
                renderedPlainBody,
                template.Code,
                template.Version,
                missingVariables);
        }

        var renderedBody = ReplaceVariables(template.BodyTemplate, lookup, allowedSet, htmlEncodeValues: false).Trim();
        return new RenderedNotificationContent(
            renderedSubject,
            renderedBody,
            PlainTextBody: null,
            template.Code,
            template.Version,
            missingVariables);
    }

    public void ValidateTemplateDefinition(
        string subjectTemplate,
        string bodyTemplate,
        string? plainTextTemplate,
        IReadOnlyCollection<string> allowedVariables,
        NotificationChannel channel)
    {
        var allowed = new HashSet<string>(
            allowedVariables.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()),
            StringComparer.Ordinal);

        var invalidVars = new List<string>();
        CollectInvalidVariables(subjectTemplate, allowed, invalidVars);
        CollectInvalidVariables(bodyTemplate, allowed, invalidVars);
        if (!string.IsNullOrWhiteSpace(plainTextTemplate))
        {
            CollectInvalidVariables(plainTextTemplate, allowed, invalidVars);
        }

        if (invalidVars.Count > 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [NotificationErrorCodes.TemplateInvalidVariables] =
                [
                    $"La plantilla utiliza variables no declaradas o con sintaxis inválida: {string.Join(", ", invalidVars.Distinct())}."
                ]
            });
        }

        if (HasUnsafeContent(subjectTemplate) ||
            HasUnsafeContent(bodyTemplate) ||
            (!string.IsNullOrWhiteSpace(plainTextTemplate) && HasUnsafeContent(plainTextTemplate)))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [NotificationErrorCodes.TemplateUnsafeContent] =
                [
                    "La plantilla contiene etiquetas o atributos HTML/JavaScript no permitidos por seguridad."
                ]
            });
        }
    }

    public static bool HasUnsafeContent(string? content) =>
        !string.IsNullOrWhiteSpace(content) && UnsafeMarkupRegex.IsMatch(content);

    public static IReadOnlyList<string> ExtractInvalidVariables(string? templateText, IReadOnlySet<string> allowedVariables)
    {
        var invalid = new List<string>();
        if (string.IsNullOrWhiteSpace(templateText))
        {
            return invalid;
        }

        CollectInvalidVariables(templateText, allowedVariables, invalid);
        return invalid;
    }

    private static void CollectInvalidVariables(string? text, IReadOnlySet<string> allowedVariables, List<string> invalidVars)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (Match match in AnyBracesRegex.Matches(text))
        {
            var rawName = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(rawName) || !Regex.IsMatch(rawName, @"^[a-zA-Z0-9_]+$") || !allowedVariables.Contains(rawName))
            {
                invalidVars.Add(string.IsNullOrWhiteSpace(rawName) ? "{{}}" : rawName);
            }
        }
    }

    private static string ReplaceVariables(
        string templateText,
        IReadOnlyDictionary<string, string?> variables,
        IReadOnlySet<string> allowedSet,
        bool htmlEncodeValues)
    {
        if (string.IsNullOrEmpty(templateText))
        {
            return string.Empty;
        }

        return PlaceholderRegex.Replace(templateText, match =>
        {
            var key = match.Groups[1].Value;
            if (allowedSet.Count > 0 && !allowedSet.Contains(key))
            {
                return string.Empty;
            }

            if (!variables.TryGetValue(key, out var rawValue) || string.IsNullOrEmpty(rawValue))
            {
                return string.Empty;
            }

            return htmlEncodeValues ? WebUtility.HtmlEncode(rawValue) : rawValue;
        });
    }

    // Variables que la plantilla usa pero que quien despachó no informó (un valor vacío sí cuenta como informado).
    private static IReadOnlyList<string> FindMissingVariables(
        string templateText,
        IReadOnlyDictionary<string, string?> variables,
        IReadOnlySet<string> allowedSet) =>
        PlaceholderRegex.Matches(templateText)
            .Select(m => m.Groups[1].Value)
            .Where(key => (allowedSet.Count == 0 || allowedSet.Contains(key)) && !variables.ContainsKey(key))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string StripHtmlTags(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withLineBreaks = Regex.Replace(html, @"<\s*\/?\s*(p|br|div|li)\b[^>]*>", " ", RegexOptions.IgnoreCase);
        var stripped = HtmlTagsRegex.Replace(withLineBreaks, string.Empty);
        return Regex.Replace(stripped, @"\s+", " ").Trim();
    }
}
