namespace RiotAutoLogin;

/// <summary>The popup that asks which account to log in with.</summary>
sealed class PickerForm : Form
{
    readonly ListBox _list = new();
    public Account? Selected { get; private set; }

    public PickerForm(List<Account> accounts)
    {
        Text = "Riot Client – choose account";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(300, 270);

        _list.Left = 10; _list.Top = 10; _list.Width = 280; _list.Height = 210;
        _list.Font = new Font(Font.FontFamily, 11f);
        _list.DisplayMember = nameof(Account.Label);
        _list.DataSource = accounts;
        _list.DoubleClick += (_, _) => Choose();

        var login = new Button { Text = "Log in", Left = 10, Top = 230, Width = 135 };
        login.Click += (_, _) => Choose();

        var skip = new Button { Text = "Skip", Left = 155, Top = 230, Width = 135, DialogResult = DialogResult.Cancel };

        Controls.AddRange(new Control[] { _list, login, skip });
        AcceptButton = login;
        CancelButton = skip;
        Shown += (_, _) => { Activate(); _list.Focus(); };
    }

    void Choose()
    {
        if (_list.SelectedItem is Account a)
        {
            Selected = a;
            DialogResult = DialogResult.OK;
        }
    }
}

/// <summary>Add / edit / delete saved accounts.</summary>
sealed class ManagerForm : Form
{
    readonly List<Account> _accounts = Store.Load();
    readonly ListBox _list = new();
    readonly TextBox _label = new();
    readonly TextBox _user = new();
    readonly TextBox _pass = new();

    public ManagerForm()
    {
        Text = "Riot Auto Login – accounts";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(510, 290);

        _list.Left = 10; _list.Top = 10; _list.Width = 200; _list.Height = 240;
        _list.SelectedIndexChanged += (_, _) => LoadSelected();

        _pass.UseSystemPasswordChar = true;
        AddField("Label (shown in the picker)", _label, 10);
        AddField("Riot username", _user, 65);
        AddField("Password", _pass, 120);

        var save = new Button { Text = "Save", Left = 230, Top = 185, Width = 80 };
        var neu = new Button { Text = "New", Left = 320, Top = 185, Width = 80 };
        var del = new Button { Text = "Delete", Left = 410, Top = 185, Width = 80 };
        save.Click += (_, _) => SaveCurrent();
        neu.Click += (_, _) => { _list.ClearSelected(); _label.Clear(); _user.Clear(); _pass.Clear(); _label.Focus(); };
        del.Click += (_, _) => DeleteCurrent();

        var note = new Label
        {
            Text = "Passwords are encrypted with Windows DPAPI\nand can only be read by your Windows user.",
            Left = 230, Top = 225, Width = 270, Height = 40
        };

        Controls.AddRange(new Control[] { _list, save, neu, del, note });
        Refresh2();
    }

    void AddField(string caption, TextBox box, int top)
    {
        Controls.Add(new Label { Text = caption, Left = 230, Top = top, Width = 260 });
        box.Left = 230; box.Top = top + 20; box.Width = 260;
        Controls.Add(box);
    }

    void Refresh2()
    {
        _list.DataSource = null;
        _list.DataSource = _accounts;
        _list.DisplayMember = nameof(Account.Label);
    }

    void LoadSelected()
    {
        if (_list.SelectedItem is Account a)
        {
            _label.Text = a.Label;
            _user.Text = a.Username;
            _pass.Text = a.Password;
        }
    }

    void SaveCurrent()
    {
        if (string.IsNullOrWhiteSpace(_label.Text) || string.IsNullOrWhiteSpace(_user.Text) || _pass.Text.Length == 0)
        {
            MessageBox.Show(this, "Please fill in label, username and password.", "Riot Auto Login");
            return;
        }

        if (_list.SelectedItem is Account existing)
        {
            existing.Label = _label.Text.Trim();
            existing.Username = _user.Text.Trim();
            existing.Password = _pass.Text;
        }
        else
        {
            _accounts.Add(new Account { Label = _label.Text.Trim(), Username = _user.Text.Trim(), Password = _pass.Text });
        }

        Store.Save(_accounts);
        Refresh2();
    }

    void DeleteCurrent()
    {
        if (_list.SelectedItem is not Account a) return;
        if (MessageBox.Show(this, $"Delete \"{a.Label}\"?", "Riot Auto Login", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _accounts.Remove(a);
        Store.Save(_accounts);
        _label.Clear(); _user.Clear(); _pass.Clear();
        Refresh2();
    }
}
