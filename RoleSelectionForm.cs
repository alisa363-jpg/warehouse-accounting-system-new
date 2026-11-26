namespace Warehouse;

public class RoleSelectionForm : Form
{
    public static IReadOnlyList<string> Roles { get; } = Array.AsReadOnly(new[]
    {
        "Менеджер по заказам", "Менеджер по закупкам", "Кладовщик", "Бухгалтер", "Курьер"
    });

    public string? SelectedRole { get; private set; }

    public RoleSelectionForm()
    {
        Text = "Складской учёт — выбор рабочего места";
        ClientSize = new Size(460, 450);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.Controls.Add(new Label
        {
            Text = "Выберите рабочее место\nПароль не требуется",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter
        });
        foreach (string role in Roles)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
            var button = new Button { Text = role, Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 6) };
            button.Click += (_, _) => { SelectedRole = role; DialogResult = DialogResult.OK; };
            layout.Controls.Add(button);
        }
        Controls.Add(layout);
    }
}
