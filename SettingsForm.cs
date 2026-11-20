namespace KeyboardLayoutSwitcher;

// Окно настроек: список правил (exe → раскладка), автовыбор exe активного окна,
// список доступных раскладок, автозапуск, уведомления.
internal partial class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly TrayApplicationContext _owner;
    private bool _dirty;

    private DataGridView _grid = null!;
    private ComboBox _layoutCombo = null!;
    private CheckBox _autoStartCheck = null!;
    private CheckBox _balloonCheck = null!;
    private Button _addButton = null!;
    private Button _removeButton = null!;
    private Button _pickButton = null!;
    private Button _saveButton = null!;
    private Button _cancelButton = null!;

    public SettingsForm(AppSettings settings, TrayApplicationContext owner)
    {
        _settings = settings;
        _owner = owner;
        BuildUi();
        LoadRules();
    }

    private void BuildUi()
    {
        Text = "Keyboard Layout Switcher — Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(560, 470);
        Font = new Font("Segoe UI", 9F);

        var label1 = new Label
        {
            Text = "Rules: when a window of the process below becomes active, " +
                   "the selected keyboard layout is switched on.",
            Location = new Point(12, 12),
            Size = new Size(530, 32),
        };

        _grid = new DataGridView
        {
            Location = new Point(12, 48),
            Size = new Size(530, 220),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        _grid.Columns.Add("Process", "Process (exe)");
        _grid.Columns.Add("Layout", "Layout");
        _grid.Columns["Process"]!.FillWeight = 65;
        _grid.Columns["Layout"]!.FillWeight = 35;
        _grid.CellValueChanged += (s, e) => _dirty = true;
        _grid.UserDeletedRow += (s, e) => _dirty = true;
        _grid.DataError += (s, e) => e.ThrowException = false;

        _addButton = new Button { Text = "Add rule", Location = new Point(12, 278), Size = new Size(100, 28) };
        _addButton.Click += (s, e) => AddRule();

        _removeButton = new Button { Text = "Remove", Location = new Point(118, 278), Size = new Size(100, 28) };
        _removeButton.Click += (s, e) => RemoveSelectedRule();

        _pickButton = new Button
        {
            Text = "Pick active window's exe",
            Location = new Point(224, 278),
            Size = new Size(170, 28),
        };
        _pickButton.Click += (s, e) => PickActiveWindowExe();

        var layoutLabel = new Label
        {
            Text = "Layouts installed in this system:",
            Location = new Point(12, 318),
            AutoSize = true,
        };

        _layoutCombo = new ComboBox
        {
            Location = new Point(200, 315),
            Size = new Size(120, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        foreach (string code in LayoutService.GetAvailableLayoutCodes())
            _layoutCombo.Items.Add(code);
        if (_layoutCombo.Items.Count > 0) _layoutCombo.SelectedIndex = 0;

        _autoStartCheck = new CheckBox
        {
            Text = "Start with Windows (current user)",
            Location = new Point(12, 348),
            AutoSize = true,
        };
        _autoStartCheck.Checked = AutoStart.IsEnabled();
        _autoStartCheck.CheckedChanged += (s, e) => _dirty = true;

        _balloonCheck = new CheckBox
        {
            Text = "Show balloon tip when layout is switched",
            Location = new Point(12, 372),
            AutoSize = true,
        };
        _balloonCheck.Checked = _settings.ShowBalloonOnSwitch;
        _balloonCheck.CheckedChanged += (s, e) => _dirty = true;

        _saveButton = new Button { Text = "Save", Location = new Point(360, 400), Size = new Size(85, 30) };
        _saveButton.Click += (s, e) => SaveAndClose();

        _cancelButton = new Button { Text = "Cancel", Location = new Point(455, 400), Size = new Size(85, 30) };
        _cancelButton.Click += (s, e) => CancelClicked();

        Controls.AddRange(new Control[] { label1, _grid, _addButton, _removeButton, _pickButton,
            layoutLabel, _layoutCombo, _autoStartCheck, _balloonCheck, _saveButton, _cancelButton });
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    // ---------- Данные ----------

    private void LoadRules()
    {
        _grid.Rows.Clear();
        foreach (var rule in _settings.Rules)
            _grid.Rows.Add(rule.ProcessName, rule.Layout);
        _dirty = false;
    }

    private void AddRule()
    {
        string layout = _layoutCombo.SelectedItem?.ToString() ?? "EN";
        _grid.Rows.Add("notepad.exe", layout);
        _dirty = true;
    }

    private void RemoveSelectedRule()
    {
        if (_grid.CurrentRow != null && !_grid.CurrentRow.IsNewRow)
        {
            _grid.Rows.Remove(_grid.CurrentRow);
            _dirty = true;
        }
    }

    // Добавляет правило для exe активного окна — удобно, чтобы не угадывать имя файла.
    private void PickActiveWindowExe()
    {
        // Даём пользователю 3 секунды переключиться на нужное окно
        var picker = new PickerForm();
        if (picker.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(picker.PickedExeName))
        {
            string layout = _layoutCombo.SelectedItem?.ToString() ?? "EN";
            // Если правило для этого exe уже есть — просто выделяем его
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Cells[0].Value?.ToString()?.Trim().ToLowerInvariant() == picker.PickedExeName)
                {
                    row.Selected = true;
                    return;
                }
            }
            _grid.Rows.Add(picker.PickedExeName, layout);
            _dirty = true;
        }
    }

    // Отмена с проверкой несохранённых изменений.
    private void CancelClicked()
    {
        if (_dirty)
        {
            var answer = MessageBox.Show(this, "There are unsaved changes. Save them before closing?",
                "Keyboard Layout Switcher", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes) { SaveAndClose(); return; }
            if (answer == DialogResult.Cancel) return;
        }
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void SaveAndClose()
    {
        _settings.Rules.Clear();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            string proc = row.Cells[0].Value?.ToString()?.Trim().ToLowerInvariant() ?? "";
            string layout = row.Cells[1].Value?.ToString()?.Trim().ToUpperInvariant() ?? "";
            if (proc.Length > 0 && layout.Length > 0)
                _settings.Rules.Add(new LayoutRule { ProcessName = proc, Layout = layout });
        }
        _settings.ShowBalloonOnSwitch = _balloonCheck.Checked;
        _settings.Save();

        try { AutoStart.SetEnabled(_autoStartCheck.Checked); }
        catch { MessageBox.Show(this, "Could not update the autostart registry value.",
              "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning); }

        _owner.RefreshTray();
        DialogResult = DialogResult.OK;
        Close();
    }
}

// Полупрозрачное окно «переключитесь на нужное окно»: ловит следующий foreground.
internal class PickerForm : Form
{
    private readonly System.Windows.Forms.Timer _timer = new();
    private string? _pickedExeName;
    private int _remaining = 3;

    public string? PickedExeName => _pickedExeName;

    public PickerForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(420, 90);
        BackColor = Color.White;
        Opacity = 0.92;
        TopMost = true;

        var label = new Label
        {
            Text = "Switch to the target window...",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 11F),
        };
        Controls.Add(label);

        _timer.Interval = 1000;
        _timer.Tick += (s, e) =>
        {
            _remaining--;
            if (_remaining <= 0)
            {
                _timer.Stop();
                IntPtr hwnd = NativeMethods.GetForegroundWindow();
                _pickedExeName = NativeMethods.GetProcessExeName(hwnd);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                label.Text = $"Switch to the target window... {_remaining}";
            }
        };
        _timer.Start();
    }
}
