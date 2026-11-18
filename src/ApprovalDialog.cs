// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DIsland;

/// <summary>
/// Tactical Iron Horus confirmation dialog for dangerous Codex commands &amp; file changes,
/// preserving the full JSON-RPC approval / user-input protocol contract.
/// </summary>
public static class ApprovalDialog
{
    private static readonly Dictionary<string, JsonNode> changes = new();
    private static readonly Brush AmberGuard = Frozen(Color.FromRgb(0xF2, 0xD1, 0x7D));
    private static readonly Brush AmberDarkInk = Frozen(Color.FromRgb(0x8A, 0x5B, 0x00));

    public static void TrackItem(JsonNode? item)
    {
        if (item?["type"]?.ToString() != "fileChange" || item["id"] == null) return;
        if (changes.Count > 40) changes.Clear();
        changes[item["id"]!.ToString()] = item.DeepClone();
    }

    public static Task<JsonNode?> Handle(string method, JsonNode? args) =>
        Application.Current.Dispatcher.InvokeAsync(() => Ask(method, args)).Task;

    private static JsonNode? Ask(string method, JsonNode? args)
    {
        if (!AssistantRuntime.Busy) return null;
        var approval = method is "item/commandExecution/requestApproval" or "item/fileChange/requestApproval";
        var input = method is "item/tool/requestUserInput" or "tool/requestUserInput";
        if (!approval && !input) return null;
        if (args?["threadId"]?.ToString() != AssistantRuntime.ActiveThread
            || (args?["turnId"] is { } turn && turn.ToString() != AssistantRuntime.ActiveTurn))
            return approval ? new JsonObject { ["decision"] = "decline" } : null;

        var spec = IslandSkinEngine.Current;
        var amberBrush = spec.IsLight ? AmberDarkInk : AmberGuard;

        var window = new Window
        {
            Title = approval ? "IRON HORUS · Подтверждение действия" : "IRON HORUS · Уточнение для Хошино",
            Width = 580,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 620,
            MinWidth = 460,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
        };
        AssistantTheme.Apply(window);

        var outerShell = new Border
        {
            CornerRadius = new CornerRadius(spec.IsPixelMode ? 4 : 20),
            BorderThickness = new Thickness(spec.IsCartoonSticker ? 2.6 : 1.4),
            Padding = new Thickness(18),
            Margin = new Thickness(14),
            Effect = spec.IsCartoonSticker
                ? IslandSkinEngine.CreateCartoonHardShadow(5)
                : new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 26,
                    ShadowDepth = 6,
                    Direction = 270,
                    Color = Colors.Black,
                    Opacity = 0.45,
                },
        };
        outerShell.SetResourceReference(Border.BackgroundProperty, "HoshinoBackground");
        outerShell.SetResourceReference(Border.BorderBrushProperty, "HoshinoBorder");
        outerShell.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                try { window.DragMove(); } catch { /* ignore if released */ }
        };

        // Tactical Iron Horus inner card with 4px left amber guard spine (#F2D17D)
        var card = new Border
        {
            CornerRadius = new CornerRadius(spec.IsPixelMode ? 2 : 15),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
        };
        card.SetResourceReference(Border.BackgroundProperty, "HoshinoSurface");
        card.SetResourceReference(Border.BorderBrushProperty, "HoshinoBorder");

        var cardGrid = new Grid();
        cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4.5) });
        cardGrid.ColumnDefinitions.Add(new ColumnDefinition());

        var amberSpine = new Border { Background = AmberGuard };
        cardGrid.Children.Add(amberSpine);

        var body = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
        Grid.SetColumn(body, 1);
        cardGrid.Children.Add(body);

        // Tactical Shield Header: [🛡] IRON HORUS · ПОДТВЕРЖДЕНИЕ ДЕЙСТВИЯ
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        headerRow.Children.Add(CreateIronHorusShieldIcon(amberBrush));
        headerRow.Children.Add(new TextBlock
        {
            Text = approval ? "IRON HORUS · ПОДТВЕРЖДЕНИЕ ДЕЙСТВИЯ" : "IRON HORUS · НУЖНО УТОЧНЕНИЕ",
            FontSize = 12.5,
            FontWeight = FontWeights.Bold,
            Foreground = amberBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 0, 0),
        });
        body.Children.Add(headerRow);

        var answers = new Dictionary<string, TextBox>();
        if (approval)
        {
            var subtitle = new TextBlock
            {
                Text = method == "item/fileChange/requestApproval"
                    ? "Помощник запрашивает разрешение на изменение файлов. Проверьте список путей перед выполнением."
                    : "Помощник запрашивает выполнение команды в терминале. Проверьте команду и рабочую папку.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 0, 12),
            };
            subtitle.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoMuted");
            body.Children.Add(subtitle);

            var details = args?.DeepClone();
            if (method == "item/fileChange/requestApproval"
                && args?["itemId"] is { } itemId
                && changes.TryGetValue(itemId.ToString(), out var change)
                && details is JsonObject obj)
            {
                obj["proposedChanges"] = change.DeepClone();
            }

            var summaryText = ExtractTacticalSummary(method, details);
            var codeBox = new Border
            {
                CornerRadius = new CornerRadius(spec.IsPixelMode ? 2 : 10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 10),
            };
            codeBox.SetResourceReference(Border.BackgroundProperty, "HoshinoBackground");
            codeBox.SetResourceReference(Border.BorderBrushProperty, "HoshinoBorder");

            var mono = new TextBox
            {
                Text = summaryText,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12.5,
                MaxHeight = 180,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            mono.SetResourceReference(Control.ForegroundProperty, "HoshinoText");
            codeBox.Child = mono;
            body.Children.Add(codeBox);

            // Optional raw JSON expander for full auditability
            var rawJson = details?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}";
            var rawBox = new TextBox
            {
                Text = rawJson,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 11,
                MaxHeight = 140,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 6, 0, 0),
            };
            var expander = new Expander
            {
                Header = new TextBlock { Text = "Подробности JSON", FontSize = 11 },
                Content = rawBox,
                Margin = new Thickness(0, 0, 0, 6),
            };
            ((TextBlock)expander.Header).SetResourceReference(TextBlock.ForegroundProperty, "HoshinoMuted");
            body.Children.Add(expander);
        }
        else if (args?["questions"] is JsonArray questions)
        {
            var scrollContent = new StackPanel();
            foreach (var q in questions)
            {
                var qText = new TextBlock
                {
                    Text = q?["question"]?.ToString(),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 6, 0, 6),
                };
                qText.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoText");
                scrollContent.Children.Add(qText);

                var box = new TextBox { MinHeight = 34 };
                answers[q!["id"]!.ToString()] = box;
                scrollContent.Children.Add(box);

                if (q["options"] is JsonArray options)
                {
                    var optWrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
                    foreach (var option in options)
                    {
                        var label = option?["label"]?.ToString() ?? "";
                        var b = new Button { Content = label, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 5, 10, 5) };
                        b.Click += (_, _) => box.Text = label;
                        optWrap.Children.Add(b);
                    }
                    scrollContent.Children.Add(optWrap);
                }
            }
            body.Children.Add(new ScrollViewer { Content = scrollContent, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }

        // High-contrast action buttons: [Разрешить] (Accent pill) + [Отмена] (Control pill)
        var buttonGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        buttonGrid.ColumnDefinitions.Add(new ColumnDefinition());
        buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        buttonGrid.ColumnDefinitions.Add(new ColumnDefinition());

        var accept = MakePillButton(
            text: approval ? "Разрешить" : "Ответить",
            primary: true,
            cornerRadius: spec.IsPixelMode ? 2 : 18);
        accept.IsDefault = true;
        accept.Click += (_, _) => window.DialogResult = true;
        if (args?["availableDecisions"] is JsonArray decisions && !decisions.Any(d => d?.ToString() == "accept"))
            accept.IsEnabled = false;

        var cancel = MakePillButton(
            text: "Отмена",
            primary: false,
            cornerRadius: spec.IsPixelMode ? 2 : 18);
        cancel.IsCancel = true;
        cancel.Click += (_, _) => window.DialogResult = false;

        Grid.SetColumn(accept, 0);
        Grid.SetColumn(cancel, 2);
        buttonGrid.Children.Add(accept);
        buttonGrid.Children.Add(cancel);
        body.Children.Add(buttonGrid);

        card.Child = cardGrid;
        outerShell.Child = card;
        window.Content = outerShell;

        var accepted = window.ShowDialog() == true && AssistantRuntime.Busy;
        if (approval)
            return new JsonObject { ["decision"] = accepted ? "accept" : "decline" };

        var map = new JsonObject();
        foreach (var pair in answers)
            map[pair.Key] = new JsonObject { ["answers"] = new JsonArray(accepted ? pair.Value.Text : "Отмена") };
        return new JsonObject { ["answers"] = map };
    }

    private static string ExtractTacticalSummary(string method, JsonNode? details)
    {
        if (details is null) return "Описание отсутствует";
        var sb = new StringBuilder();

        if (details["command"] is JsonArray cmdArray)
        {
            sb.AppendLine(string.Join(" ", cmdArray.Select(x => x?.ToString())));
        }
        else if (details["command"] is { } cmdNode)
        {
            sb.AppendLine(cmdNode.ToString());
        }

        if (details["cwd"]?.ToString() is { Length: > 0 } cwd)
            sb.AppendLine($"Папка: {cwd}");

        if (details["reason"]?.ToString() is { Length: > 0 } reason)
            sb.AppendLine($"Причина: {reason}");

        if (method == "item/fileChange/requestApproval" && details["proposedChanges"]?["changes"] is JsonArray fileChanges)
        {
            foreach (var fc in fileChanges)
            {
                var kind = fc?["kind"]?.ToString() ?? "edit";
                var path = fc?["path"]?.ToString() ?? "";
                if (path.Length > 0) sb.AppendLine($"[{kind}] {path}");
            }
        }

        var text = sb.ToString().Trim();
        return text.Length > 0
            ? text
            : details.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static Viewbox CreateIronHorusShieldIcon(Brush strokeBrush)
    {
        var c = new Canvas { Width = 20, Height = 20, IsHitTestVisible = false };
        c.Children.Add(new Path
        {
            Data = Geometry.Parse("M 10,1.5 L 17.5,4.5 V 10.2 C 17.5,14.8 14.2,17.8 10,19.0 C 5.8,17.8 2.5,14.8 2.5,10.2 V 4.5 Z"),
            Fill = Frozen(Color.FromArgb(0x28, 0xF2, 0xD1, 0x7D)),
            Stroke = strokeBrush,
            StrokeThickness = 1.65,
            StrokeLineJoin = PenLineJoin.Round,
        });
        c.Children.Add(new Path
        {
            Data = Geometry.Parse("M 6.8,10.2 L 9.2,12.6 L 13.5,7.8"),
            Stroke = strokeBrush,
            StrokeThickness = 1.65,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        });
        return new Viewbox { Width = 18, Height = 18, Child = c };
    }

    private static Button MakePillButton(string text, bool primary, double cornerRadius)
    {
        var btn = new Button
        {
            Height = 38,
            Cursor = Cursors.Hand,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Content = text,
        };

        var tpl = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border), "bd");
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
        bd.SetValue(Border.BorderThicknessProperty, new Thickness(primary ? 0 : 1.2));
        bd.SetValue(Border.BackgroundProperty, new DynamicResourceExtension(primary ? "HoshinoAccent" : "HoshinoControl"));
        if (!primary)
            bd.SetValue(Border.BorderBrushProperty, new DynamicResourceExtension("HoshinoBorder"));

        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        tpl.VisualTree = bd;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.88, "bd"));
        tpl.Triggers.Add(hover);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4, "bd"));
        tpl.Triggers.Add(disabled);

        btn.Template = tpl;
        btn.SetResourceReference(Control.ForegroundProperty, primary ? "HoshinoOnAccent" : "HoshinoText");
        return btn;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
