// Original HoshiBar code: Copyright (c) 2026 Takayom1
// SPDX-License-Identifier: MIT
// Scope and artwork exclusions: see LICENSING.md.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DIsland.Services;

namespace DIsland;

/// <summary>
/// Coworking companion bar beneath Focus Timer: 1-click task goal binding while Hoshino wears focus headphones.
/// Uses dynamic HoshinoPalette resources so every visual skin styles it automatically.
/// </summary>
public sealed class CoworkingBar : Border
{
    private readonly AppSettings settings;
    private readonly NotesService notes;
    private readonly TimerService timer;
    private readonly DockPanel goalRow = new();

    public event Action? LayoutChanged;
    public event Action? FocusTaskChanged;

    public CoworkingBar(AppSettings settings, NotesService notes, TimerService timer)
    {
        this.settings = settings;
        this.notes = notes;
        this.timer = timer;

        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(12, 8, 12, 8);
        Margin = new Thickness(0, 8, 0, 0);
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "HoshinoSurface");
        SetResourceReference(BorderBrushProperty, "HoshinoBorder");
        MouseLeftButtonUp += (_, e) => e.Handled = true;

        Child = goalRow;

        notes.Changed += RefreshGoal;
        timer.StateChanged += RefreshGoal;
        RefreshGoal();
    }

    public void BindTask(Guid? id)
    {
        settings.FocusTaskId = settings.FocusTaskId == id ? null : id;
        settings.Save();
        RefreshGoal();
        FocusTaskChanged?.Invoke();
    }

    public void RefreshGoal()
    {
        goalRow.Children.Clear();
        var openTasks = notes.Todos.Where(t => !t.Done).ToList();
        var bound = settings.FocusTaskId is { } id ? openTasks.FirstOrDefault(t => t.Id == id) : null;

        if (bound is null && settings.FocusTaskId is not null)
        {
            settings.FocusTaskId = null;
            settings.Save();
            FocusTaskChanged?.Invoke();
        }

        if (bound is not null)
        {
            Visibility = Visibility.Visible;
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(actions, Dock.Right);

            var doneBtn = new Button
            {
                Style = (Style)Application.Current.FindResource("IconButton"),
                Width = 24,
                Height = 22,
                Cursor = Cursors.Hand,
                ToolTip = "Завершить задачу фокуса",
                Content = VectorIcon("M 2.5,8.2 L 6.2,11.8 L 13.5,4.2", "HoshinoAccent"),
            };
            var boundId = bound.Id;
            doneBtn.Click += (_, _) => notes.SetDone(boundId, true);

            var clearBtn = new Button
            {
                Style = (Style)Application.Current.FindResource("IconButton"),
                Width = 24,
                Height = 22,
                Cursor = Cursors.Hand,
                Margin = new Thickness(2, 0, 0, 0),
                ToolTip = "Отвязать цель фокуса",
                Content = VectorIcon("M 4,4 L 12,12 M 12,4 L 4,12", "HoshinoMuted"),
            };
            clearBtn.Click += (_, _) => BindTask(null);

            actions.Children.Add(doneBtn);
            actions.Children.Add(clearBtn);
            goalRow.Children.Add(actions);

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(VectorIcon("M 3,9 C 3,4.5 13,4.5 13,9 M 2.5,8.5 V 11.5 M 13.5,8.5 V 11.5", "HoshinoAccent"));
            var badgeText = new TextBlock
            {
                Text = (timer.IsActive ? "Коворкинг · " : "Цель · ") + bound.Text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(6, 0, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 380,
                VerticalAlignment = VerticalAlignment.Center,
            };
            badgeText.SetResourceReference(TextBlock.ForegroundProperty, "HoshinoAccent");
            left.Children.Add(badgeText);
            goalRow.Children.Add(left);
        }
        else if (openTasks.Count > 0)
        {
            Visibility = Visibility.Visible;
            var first = openTasks[0];
            var quickBind = new Button
            {
                Style = (Style)Application.Current.FindResource("ChipButton"),
                Content = "Взять в фокус",
                Cursor = Cursors.Hand,
                ToolTip = $"Привязать задачу «{first.Text}» к таймеру в 1 клик",
                Padding = new Thickness(9, 3, 9, 3),
                FontSize = 11,
            };
            DockPanel.SetDock(quickBind, Dock.Right);
            var targetId = first.Id;
            quickBind.Click += (_, _) => BindTask(targetId);
            goalRow.Children.Add(quickBind);

            var hint = new TextBlock
            {
                Text = "Коворкинг · " + first.Text,
                FontSize = 11.5,
                Opacity = 0.65,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 8, 0),
            };
            goalRow.Children.Add(hint);
        }
        else
        {
            Visibility = Visibility.Collapsed;
        }

        LayoutChanged?.Invoke();
    }

    private static Path VectorIcon(string data, string brushResource)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 16,
            Height = 16,
            Stretch = Stretch.None,
            VerticalAlignment = VerticalAlignment.Center,
        };
        path.SetResourceReference(Shape.StrokeProperty, brushResource);
        return path;
    }
}
