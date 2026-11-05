// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt

using Meltype.Config;

namespace Meltype.Tests;

internal static class SettingsFormTests
{
    [Test]
    public static void SettingsForm_FontCanBeChangedWithoutOpeningDropDown()
    {
        using var engine = new MeltypeEngine(new Settings(), null, null);
        using var form = new UI.SettingsForm(engine, new AppState());
        var combo = Descendants(form).OfType<ComboBox>().Single(c => c.AccessibleName == "変換ボックスのフォント");
        Assert.True(combo.Items.Count > 1, "開いた時点で、この PC のフォントを選べる");
        Assert.Equal(0, combo.SelectedIndex, "既定のフォントから始まる");
        form.Show();
        combo.Focus();
        Assert.True(!combo.DroppedDown, "一覧を開いていない");
        Native.SendMessage(combo.Handle, Native.WM_KEYDOWN, (nint)(int)Keys.Down, 0);
        Native.SendMessage(combo.Handle, Native.WM_KEYUP, (nint)(int)Keys.Down, 0);
        Assert.Equal(1, combo.SelectedIndex, "閉じたまま ↓ で次のフォントを選べる");
        var collect = form.GetType().GetMethod("Collect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Assert.Equal(combo.GetItemText(combo.Items[1]), ((Settings)collect.Invoke(form, null)!).CompositionFont, "選んだフォントを設定に戻せる");
    }

    [Test]
    public static void SettingsForm_RowsDoNotOverlapAfterOpeningAndResizing()
    {
        using var engine = new MeltypeEngine(new Settings(), null, null);
        using var form = new UI.SettingsForm(engine, new AppState());
        form.Show();
        foreach (var width in new[] { form.Width, form.MinimumSize.Width })
        {
            form.Width = width;
            form.PerformLayout();
            var groups = Descendants(form).OfType<GroupBox>().Where(g => g.Controls.OfType<TableLayoutPanel>().Any()).ToList();
            Assert.True(groups.Count > 1, "分類ごとの枠を作っている");
            foreach (var group in groups)
            {
                var table = group.Controls.OfType<TableLayoutPanel>().Single();
                Assert.True(table.Bottom <= group.ClientSize.Height, $"分類の枠から表がはみ出さない: {group.Text}");
                var bottom = 0;
                foreach (var row in table.Controls.Cast<Control>().GroupBy(c => table.GetPositionFromControl(c).Row).OrderBy(r => r.Key))
                {
                    Assert.True(row.Key >= 0 && row.All(c => c.Height > 0), "各項目を行に配置している");
                    Assert.True(row.Min(c => c.Top) >= bottom, $"前の行と重ならない: {group.Text} の {row.Key} 行目 (幅 {width})");
                    bottom = row.Max(c => c.Bottom);
                }
            }
        }
    }

    [Test]
    public static void SettingsForm_CustomKindsLoadAndCanStillBeEdited()
    {
        using var engine = new MeltypeEngine(new Settings
        {
            AppKinds = [new() { Name = "チャット", LiveConversion = false }, new() { Name = "仕事", Base = AppProfile.Code }],
            AppRules = [new() { Process = "chat.exe", Kind = "チャット" }],
        }, null, null);
        using var form = new UI.SettingsForm(engine, new AppState());
        var controls = Descendants(form).ToList();
        var rules = controls.OfType<DataGridView>().Single(g => g.Columns[0].HeaderText.StartsWith("プロセス名", StringComparison.Ordinal));
        var kinds = controls.OfType<DataGridView>().Single(g => g.AccessibleName == "独自の種類");
        Assert.Equal("チャット", rules.Rows[0].Cells[2].Value, "アプリの種類を読み込める");
        var collect = form.GetType().GetMethod("Collect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        Assert.Equal(engine.Settings.Clone().Normalize().ToJson(), ((Settings)collect.Invoke(form, null)!).ToJson(), "読み込んだ値を失わない");
        kinds.Rows[0].Cells[0].Value = "雑談";
        var choices = (DataGridViewComboBoxColumn)rules.Columns[2];
        Assert.True(choices.Items.Contains("雑談"), "読み込み後の編集は、アプリ別設定の選択肢にすぐ反映する");
    }

    [Test]
    public static void SettingsForm_ZenzaiPathsCollectEditsAndEmptyValues()
    {
        var settings = new Settings
        {
            ZenzaiServerPath = @"C:\Zenzai tools\llama-server.exe",
            ZenzaiModelPath = @"C:\モデル\zenz.gguf",
        };
        using var engine = new MeltypeEngine(settings, null, null);
        using var form = new UI.SettingsForm(engine, new AppState());
        var controls = Descendants(form).ToList();
        var server = controls.OfType<TextBox>().Single(c => c.AccessibleName == "Zenzai: llama-server の場所");
        var model = controls.OfType<TextBox>().Single(c => c.AccessibleName == "Zenzai: モデル (GGUF) の場所");
        Assert.Equal(settings.ZenzaiServerPath, server.Text, "既存の実行ファイルの場所を読み込める");
        Assert.Equal(settings.ZenzaiModelPath, model.Text, "日本語を含むモデルの場所を読み込める");
        form.Show();
        foreach (var width in new[] { form.Width, form.MinimumSize.Width })
        {
            form.Width = width;
            form.PerformLayout();
            foreach (var box in new[] { server, model })
            {
                var pick = box.Parent!.Controls.OfType<Button>().Single();
                Assert.True(box.Width > 0 && box.Right <= pick.Left, "狭いウィンドウで参照ボタンが入力欄と重ならない");
                Assert.True(pick.Right <= box.Parent.ClientSize.Width, "参照ボタンが枠からはみ出さない");
            }
        }
        server.Text = @"D:\別の場所\llama-server.exe";
        model.Text = @"D:\別の場所\zenz.gguf";
        var collect = form.GetType().GetMethod("Collect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var next = (Settings)collect.Invoke(form, null)!;
        Assert.Equal(server.Text, next.ZenzaiServerPath, "手入力した実行ファイルの場所を設定に取り込める");
        Assert.Equal(model.Text, next.ZenzaiModelPath, "手入力したモデルの場所を設定に取り込める");
        server.Clear();
        model.Clear();
        next = (Settings)collect.Invoke(form, null)!;
        Assert.Equal("", next.ZenzaiServerPath, "実行ファイルの場所を空欄に戻すと設定値も空文字になる");
        Assert.Equal("", next.ZenzaiModelPath, "モデルの場所を空欄に戻すと設定値も空文字になる");
    }

    [Test]
    public static void SettingsForm_ZenzaiPathsAcceptSingleDroppedFile()
    {
        var directory = Directory.CreateTempSubdirectory("meltype-file-drop-");
        try
        {
            var serverPath = Path.Combine(directory.FullName, "変換 tools.exe");
            var modelPath = Path.Combine(directory.FullName, "変換 model.gguf");
            File.WriteAllText(serverPath, "");
            File.WriteAllText(modelPath, "");
            using var engine = new MeltypeEngine(new Settings(), null, null);
            using var form = new UI.SettingsForm(engine, new AppState());
            var boxes = Descendants(form).OfType<TextBox>().ToList();
            var server = boxes.Single(c => c.AccessibleName == "Zenzai: llama-server の場所");
            var model = boxes.Single(c => c.AccessibleName == "Zenzai: モデル (GGUF) の場所");
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            void DragAndDrop(TextBox box, DataObject data, DragDropEffects expected)
            {
                var args = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
                typeof(Control).GetMethod("OnDragEnter", flags)!.Invoke(box, [args]);
                Assert.Equal(expected, args.Effect, "受け付けるデータだけコピーとして表示する");
                typeof(Control).GetMethod("OnDragDrop", flags)!.Invoke(box, [args]);
            }
            foreach (var (box, file) in new[] { (server, serverPath), (model, modelPath) })
            {
                Assert.True(box.AllowDrop, "ファイルをドロップできる");
                DragAndDrop(box, new DataObject(DataFormats.FileDrop, new[] { file }), DragDropEffects.Copy);
                Assert.Equal(file, box.Text, "ドロップしたファイルのパスが入る");
                foreach (var data in new[]
                {
                    new DataObject(DataFormats.FileDrop, new[] { serverPath, modelPath }),
                    new DataObject(DataFormats.FileDrop, new[] { directory.FullName }),
                    new DataObject(DataFormats.FileDrop, new[] { Path.Combine(directory.FullName, "missing.gguf") }),
                    new DataObject(DataFormats.UnicodeText, file),
                })
                {
                    DragAndDrop(box, data, DragDropEffects.None);
                    Assert.Equal(file, box.Text, "ファイル1つ以外のデータでは既存のパスを保つ");
                }
            }
            var next = (Settings)form.GetType().GetMethod("Collect", flags)!.Invoke(form, null)!;
            Assert.Equal(serverPath, next.ZenzaiServerPath, "ドロップした実行ファイルのパスを設定に取り込む");
            Assert.Equal(modelPath, next.ZenzaiModelPath, "ドロップしたモデルのパスを設定に取り込む");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
