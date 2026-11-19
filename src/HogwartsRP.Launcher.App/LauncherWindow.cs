using System.Drawing;
using System.Globalization;
using HogwartsRP.GameIntegration.Abstractions;
using HogwartsRP.Launcher;
using HogwartsRP.Networking.Client;

namespace HogwartsRP.Launcher.App;

internal sealed class LauncherWindow : Form
{
    private readonly TextBox _serverName = new() { Text = "Local development", Width = 260 };
    private readonly TextBox _serverAddress = new() { Text = "http://127.0.0.1:7777", Width = 360 };
    private readonly ComboBox _savedServers = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly Button _saveServer = new() { Text = "Save server", AutoSize = true };
    private readonly Button _removeServer = new() { Text = "Remove saved server", AutoSize = true };
    private readonly Button _checkServerStatus = new() { Text = "Check server status", AutoSize = true };
    private readonly Label _serverStatus = new() { Text = "Status not checked yet.", AutoSize = true, ForeColor = Color.LightGray };
    private readonly ServerStatusClient _serverStatusClient = new();
    private readonly JsonServerProfileStore _serverProfileStore = new();
    private readonly Label _status = new() { Text = "Choose a server and connect.", AutoSize = true };
    private readonly Button _connect = new() { Text = "Connect / Sign in", AutoSize = true };
    private readonly Button _register = new() { Text = "Register character", AutoSize = true, Enabled = false };
    private readonly Button _play = new() { Text = "Play Hogwarts Legacy", AutoSize = true, Enabled = false };
    private readonly TextBox _characterName = new() { Width = 240 };
    private readonly ComboBox _house = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly TextBox _background = new() { Width = 360 };
    private readonly TextBox _biography = new() { Width = 360, Multiline = true, Height = 72, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _wandWood = new() { Width = 150 };
    private readonly TextBox _wandCore = new() { Width = 150 };
    private readonly TextBox _wandLength = new() { Width = 70 };
    private readonly TextBox _wandFlexibility = new() { Width = 150 };
    private readonly WebSocketPlayerNetworkClient _network = new();
    private ServerConnectionService? _connection;
    private ServerSelection? _selectedServer;
    private OidcJoinCredentialProvider? _oidc;
    private bool _closing;
    private Icon? _launcherIcon;

    public LauncherWindow()
    {
        Text = "HogwartsRP Launcher";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "HogwartsRP.ico");
        if (File.Exists(iconPath))
        {
            _launcherIcon = new Icon(iconPath);
            Icon = _launcherIcon;
        }
        MinimumSize = new Size(760, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(20, 24, 35);
        ForeColor = Color.GhostWhite;
        Font = new Font("Segoe UI", 10F);
        BuildLayout();
        RefreshServerProfiles();
        _savedServers.SelectedIndexChanged += (_, _) =>
        {
            if (_savedServers.SelectedItem is ServerProfile profile)
            {
                _serverName.Text = profile.Name;
                _serverAddress.Text = profile.Address;
            }
        };
        _saveServer.Click += (_, _) => SaveServerProfile();
        _removeServer.Click += (_, _) => RemoveServerProfile();
        _checkServerStatus.Click += async (_, _) => await CheckServerStatusAsync();
        _connect.Click += async (_, _) => await ConnectAsync();
        _register.Click += async (_, _) => await RegisterCharacterAsync();
        _play.Click += async (_, _) => await PlayAsync();
        FormClosed += async (_, _) =>
        {
            _closing = true;
            if (_connection is not null)
            {
                try { await _connection.DisconnectAsync(); } catch { }
            }
            _oidc?.Dispose();
            _launcherIcon?.Dispose();
            _serverStatusClient.Dispose();
        };
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, AutoScroll = true };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        var heading = new Label { Text = "HOGWARTSRP", Font = new Font("Segoe UI", 25F, FontStyle.Bold), ForeColor = Color.FromArgb(218, 190, 125), AutoSize = true };
        root.Controls.Add(heading);
        root.Controls.Add(new Label { Text = "A roleplay launcher for Hogwarts Legacy", AutoSize = true, ForeColor = Color.LightGray, Padding = new Padding(0, 0, 0, 20) });

        var server = MakeGroup("SERVER CONNECTION");
        AddRow(server, "Saved servers", _savedServers);
        var profileActions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        profileActions.Controls.Add(_saveServer);
        profileActions.Controls.Add(_removeServer);
        profileActions.Controls.Add(_checkServerStatus);
        server.Controls.Add(profileActions);
        AddRow(server, "Server name", _serverName);
        AddRow(server, "Server address", _serverAddress);
        AddRow(server, "Live status", _serverStatus);
        server.Controls.Add(_connect);
        root.Controls.Add(server);

        var character = MakeGroup("ROLEPLAY CHARACTER");
        _house.Items.AddRange(["Gryffindor", "Hufflepuff", "Ravenclaw", "Slytherin"]);
        _house.SelectedIndex = 0;
        AddRow(character, "Character name", _characterName);
        AddRow(character, "Hogwarts house", _house);
        AddRow(character, "Background (optional)", _background);
        AddRow(character, "Biography (optional)", _biography);
        AddRow(character, "Wand wood (optional)", _wandWood);
        AddRow(character, "Wand core (optional)", _wandCore);
        AddRow(character, "Wand length, inches", _wandLength);
        AddRow(character, "Wand flexibility", _wandFlexibility);
        character.Controls.Add(_register);
        root.Controls.Add(character);

        var actions = MakeGroup("GAME");
        actions.Controls.Add(_play);
        actions.Controls.Add(_status);
        root.Controls.Add(actions);

        root.Controls.Add(new Label
        {
            Text = "Note: character registration stores roleplay metadata on the selected server. It does not create a character inside Hogwarts Legacy or provide in-game multiplayer yet.",
            AutoSize = true, MaximumSize = new Size(660, 0), ForeColor = Color.Silver, Padding = new Padding(0, 16, 0, 0)
        });
    }

    private static GroupBox MakeGroup(string title) => new()
    {
        Text = title, ForeColor = Color.FromArgb(218, 190, 125), Dock = DockStyle.Top,
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14), Margin = new Padding(0, 14, 0, 14)
    };

    private static void AddRow(Control parent, string label, Control editor)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 5, 0, 5) };
        row.Controls.Add(new Label { Text = label, Width = 190, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.GhostWhite });
        row.Controls.Add(editor);
        parent.Controls.Add(row);
        parent.Controls.SetChildIndex(row, 0);
    }

    private void RefreshServerProfiles(string? selectId = null)
    {
        try
        {
            var profiles = _serverProfileStore.Load();
            _savedServers.BeginUpdate();
            _savedServers.Items.Clear();
            foreach (var profile in profiles) _savedServers.Items.Add(profile);
            _savedServers.EndUpdate();
            if (selectId is not null)
            {
                for (var index = 0; index < _savedServers.Items.Count; index++)
                    if (_savedServers.Items[index] is ServerProfile item && item.Id == selectId)
                    {
                        _savedServers.SelectedIndex = index;
                        break;
                    }
            }
            _removeServer.Enabled = _savedServers.Items.Count > 0;
        }
        catch (Exception ex)
        {
            SetStatus($"Could not load saved servers: {ex.Message}");
            _removeServer.Enabled = false;
        }
    }

    private async Task CheckServerStatusAsync()
    {
        if (!Uri.TryCreate(_serverAddress.Text.Trim(), UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            SetStatus("Enter a valid HTTP or HTTPS server address before checking status.");
            return;
        }

        _checkServerStatus.Enabled = false;
        _serverStatus.Text = "Checking…";
        try
        {
            var snapshot = await _serverStatusClient.GetStatusAsync(endpoint);
            _serverStatus.Text = $"Online · {snapshot.OnlinePlayers}/{snapshot.MaxPlayers} players";
            SetStatus($"{snapshot.ServerName} is online. Status checked {snapshot.CheckedAtUtc.ToLocalTime():t}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            _serverStatus.Text = "Unavailable";
            SetStatus($"Could not retrieve server status: {ex.Message}");
        }
        finally
        {
            if (!_closing && !IsDisposed) _checkServerStatus.Enabled = true;
        }
    }

    private void SaveServerProfile()
    {
        try
        {
            var profile = _serverProfileStore.Save(_serverName.Text, _serverAddress.Text);
            RefreshServerProfiles(profile.Id);
            SetStatus($"Saved server profile: {profile.Name}.");
        }
        catch (Exception ex) { SetStatus($"Could not save server: {ex.Message}"); }
    }

    private void RemoveServerProfile()
    {
        if (_savedServers.SelectedItem is not ServerProfile profile) return;
        if (!_serverProfileStore.Delete(profile.Id))
        {
            SetStatus("The selected saved server was not found.");
            RefreshServerProfiles();
            return;
        }
        RefreshServerProfiles();
        SetStatus($"Removed saved server: {profile.Name}.");
    }

    private async Task ConnectAsync()
    {
        if (!Uri.TryCreate(_serverAddress.Text.Trim(), UriKind.Absolute, out var endpoint))
        {
            SetStatus("Enter a valid absolute server address.");
            return;
        }
        var name = _serverName.Text.Trim();
        if (name.Length == 0) { SetStatus("Enter a server name."); return; }

        _connect.Enabled = false;
        try
        {
            _selectedServer = new ServerSelection(Guid.NewGuid().ToString("N"), name, endpoint);
            var issuer = Environment.GetEnvironmentVariable("HogwartsRP__Oidc__Authority");
            var clientId = Environment.GetEnvironmentVariable("HogwartsRP__Oidc__ClientId");
            if (string.IsNullOrWhiteSpace(issuer) != string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException("Set both HogwartsRP__Oidc__Authority and HogwartsRP__Oidc__ClientId to configure OIDC.");
            _oidc?.Dispose();
            _oidc = string.IsNullOrWhiteSpace(issuer) ? null : new OidcJoinCredentialProvider(issuer, clientId!);
            if (_oidc is null && !endpoint.IsLoopback)
                throw new InvalidOperationException("Configure OIDC sign-in before connecting to a remote server.");

            IJoinCredentialProvider credentials = _oidc is not null
                ? _oidc
                : new DialogJoinCredentialProvider(this);
            _connection = new ServerConnectionService(_network, credentials);
            _connection.StatusMessage += message => Ui(() => SetStatus(message));
            await _connection.ConnectAsync(_selectedServer);
            SetStatus($"Connected and authenticated as {_connection.Session?.DisplayName ?? "player"}.");
            _register.Enabled = true;
            _play.Enabled = true;
            _serverName.Enabled = false;
            _serverAddress.Enabled = false;
        }
        catch (Exception ex)
        {
            SetStatus($"Connection failed: {ex.Message}");
            _connect.Enabled = true;
        }
    }

    private async Task RegisterCharacterAsync()
    {
        if (_connection is null || _selectedServer is null) return;
        var draft = new CharacterCreationDraft
        {
            Name = _characterName.Text,
            House = _house.Text,
            Background = EmptyToNull(_background.Text),
            Biography = EmptyToNull(_biography.Text),
            WandWood = EmptyToNull(_wandWood.Text),
            WandCore = EmptyToNull(_wandCore.Text),
            WandFlexibility = EmptyToNull(_wandFlexibility.Text)
        };
        if (!string.IsNullOrWhiteSpace(_wandLength.Text))
        {
            if (decimal.TryParse(_wandLength.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var length))
                draft.WandLength = length;
            else
            {
                SetStatus("Wand length must be a number between 4 and 16.");
                return;
            }
        }
        if (!draft.TryBuildSubmission(out var submission, out var errors))
        {
            SetStatus(string.Join(" ", errors));
            return;
        }

        _register.Enabled = false;
        try
        {
            var result = await _connection.SubmitCharacterAsync(submission!);
            SetStatus(result.Success
                ? $"Character registered: {result.Character?.Name ?? submission!.Name}."
                : $"Server rejected character: {result.Error ?? "No reason provided."}");
        }
        catch (Exception ex) { SetStatus($"Character submission failed: {ex.Message}"); }
        finally { _register.Enabled = !_closing && _connection?.IsConnected == true; }
    }

    private async Task PlayAsync()
    {
        if (_selectedServer is null) return;
        _play.Enabled = false;
        try
        {
            var launcher = new LauncherService(new WindowsGameInstallationDetector(), new SteamGameLauncher());
            launcher.StatusChanged += state => Ui(() => SetStatus(state.Message));
            var result = await launcher.PlayAsync(_selectedServer);
            SetStatus(result.Message);
            if (result.State == LaunchState.Failed) _play.Enabled = true;
            else SetStatus(result.Message + " The game is not connected to the RP session yet; keep this launcher open.");
        }
        catch (Exception ex) { SetStatus($"Launch failed: {ex.Message}"); _play.Enabled = true; }
    }

    private void SetStatus(string message) => _status.Text = message;
    private void Ui(Action action)
    {
        if (_closing || IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(action); } catch (InvalidOperationException) { } }
        else action();
    }
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class DialogJoinCredentialProvider(Form owner) : IJoinCredentialProvider
    {
        public ValueTask<string> GetJoinCredentialAsync(ServerSelection server, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var dialog = new TicketDialog(server.DisplayName);
            return ValueTask.FromResult(dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Ticket : string.Empty);
        }
    }

    private sealed class TicketDialog : Form
    {
        private readonly TextBox _ticket = new() { Width = 330, UseSystemPasswordChar = true };
        public string Ticket => _ticket.Text.Trim();
        public TicketDialog(string server)
        {
            Text = "Development join ticket";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(390, 130);
            Controls.Add(new Label { Text = $"Enter the one-time ticket issued by {server}:", AutoSize = true, Left = 16, Top = 16 });
            _ticket.Left = 16; _ticket.Top = 48; Controls.Add(_ticket);
            var ok = new Button { Text = "Connect", DialogResult = DialogResult.OK, Left = 250, Top = 82 };
            Controls.Add(ok); AcceptButton = ok;
        }
    }
}
