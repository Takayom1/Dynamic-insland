// Original Hoshino Island code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace DIsland;

public static class ApprovalDialog
{
    private static readonly Dictionary<string, JsonNode> changes = new();
    public static void TrackItem(JsonNode? item)
    {
        if (item?["type"]?.ToString() != "fileChange" || item["id"] == null) return;
        if (changes.Count > 40) changes.Clear(); changes[item["id"]!.ToString()] = item.DeepClone();
    }
    public static Task<JsonNode?> Handle(string method, JsonNode? args) => Application.Current.Dispatcher.InvokeAsync(() => Ask(method, args)).Task;
    private static JsonNode? Ask(string method, JsonNode? args)
    {
        if (!AssistantRuntime.Busy) return null;
        var approval = method is "item/commandExecution/requestApproval" or "item/fileChange/requestApproval";
        var input = method is "item/tool/requestUserInput" or "tool/requestUserInput";
        if (!approval && !input) return null;
        if (args?["threadId"]?.ToString() != AssistantRuntime.ActiveThread || (args?["turnId"] is { } turn && turn.ToString() != AssistantRuntime.ActiveTurn))
            return approval ? new JsonObject { ["decision"] = "decline" } : null;
        var window = new Window { Title = approval ? "Разрешить действие помощника?" : "Помощнику нужно уточнение", Width = 660, Height = 510, MinWidth = 480, MinHeight = 330, Background = new SolidColorBrush(Color.FromRgb(22, 25, 34)), Foreground = Brushes.White, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true };
        var root = new DockPanel { Margin = new Thickness(22) }; window.Content = root;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var cancel = new Button { Content = approval ? "Отклонить" : "Отмена", Padding = new Thickness(16, 8, 16, 8), IsCancel = true };
        cancel.Click += (_, _) => window.DialogResult = false; buttons.Children.Add(cancel);
        var accept = new Button { Content = approval ? "Разрешить один раз" : "Ответить", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(10, 0, 0, 0) };
        accept.Click += (_, _) => window.DialogResult = true; buttons.Children.Add(accept);
        if (args?["availableDecisions"] is JsonArray decisions && !decisions.Any(d => d?.ToString() == "accept")) accept.IsEnabled = false;
        var content = new StackPanel(); root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        content.Children.Add(new TextBlock { Text = window.Title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        var answers = new Dictionary<string, TextBox>();
        if (approval)
        {
            content.Children.Add(new TextBlock { Text = "Проверьте команду, папку и причину. Это предложение модели; описание само по себе не гарантирует безопасность.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
            var details = args?.DeepClone();
            if (method == "item/fileChange/requestApproval" && args?["itemId"] is { } itemId && changes.TryGetValue(itemId.ToString(), out var change) && details is JsonObject obj) obj["proposedChanges"] = change.DeepClone();
            var preview = details?.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) ?? "Описание отсутствует";
            content.Children.Add(new TextBox { Text = preview, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), FontFamily = new FontFamily("Consolas"), MinHeight = 180 });
        }
        else if (args?["questions"] is JsonArray questions)
        {
            foreach (var q in questions)
            {
                content.Children.Add(new TextBlock { Text = q?["question"]?.ToString(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
                var box = new TextBox { MinHeight = 32 }; answers[q!["id"]!.ToString()] = box; content.Children.Add(box);
                if (q["options"] is JsonArray options) foreach (var option in options)
                {
                    var label = option?["label"]?.ToString() ?? ""; var b = new Button { Content = label, Margin = new Thickness(0, 4, 0, 0) }; b.Click += (_, _) => box.Text = label; content.Children.Add(b);
                }
            }
        }
        var accepted = window.ShowDialog() == true && AssistantRuntime.Busy;
        if (approval) return new JsonObject { ["decision"] = accepted ? "accept" : "decline" };
        var map = new JsonObject(); foreach (var pair in answers) map[pair.Key] = new JsonObject { ["answers"] = new JsonArray(accepted ? pair.Value.Text : "Отмена") };
        return new JsonObject { ["answers"] = map };
    }
}
