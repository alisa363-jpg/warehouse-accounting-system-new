using System.ComponentModel;
using System.Data;

namespace Warehouse;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var service = new WarehouseService(new Storage(args.Length > 0 ? args[0] : AppContext.BaseDirectory));
            while (true)
            {
                using var selection = new RoleSelectionForm();
                if (selection.ShowDialog() != DialogResult.OK) break;
                using var main = new MainForm(service, selection.SelectedRole!);
                Application.Run(main);
                if (!main.SwitchRoleRequested) break;
            }
        }
        catch (Exception e) { MessageBox.Show(e.Message, "Не удалось открыть склад"); }
    }
}

public class MainForm : Form
{
    private readonly WarehouseService service;
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly ToolStripStatusLabel status = new();
    private readonly Dictionary<string, DataGridView> grids = new();
    private readonly TextBox search = new() { Width = 300, PlaceholderText = "Клиент, дата, товар, номер или состояние" };
    private readonly TreeView tree = new() { Dock = DockStyle.Left, Width = 235 };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 30000 };
    private Database D => service.Data;
    private string Role { get; }
    public bool SwitchRoleRequested { get; private set; }

    public MainForm(WarehouseService service, string role)
    {
        this.service = service;
        if (!RoleSelectionForm.Roles.Contains(role)) throw new ArgumentException("Неизвестное рабочее место.", nameof(role));
        Role = role;
        Text = "Складской учёт — " + Role; Width = 1250; Height = 760; MinimumSize = new Size(950, 600);
        StartPosition = FormStartPosition.CenterScreen;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(5) };
        var switchRole = Button("← Сменить роль", () => { SwitchRoleRequested = true; Close(); });
        switchRole.Font = new Font(Font, FontStyle.Bold);
        top.Controls.Add(switchRole);
        top.Controls.Add(new Label { Text = Role, AutoSize = true, Padding = new Padding(10, 6, 10, 0) });
        top.Controls.Add(Button("Обновить", Reload));
        if (Role == "Кладовщик")
            top.Controls.Add(Button("Заполнить пример", () => { service.Demo(); Reload(); }));
        var bar = new StatusStrip(); bar.Items.Add(status);
        Controls.Add(tabs); Controls.Add(top); Controls.Add(bar);

        if (Role == "Кладовщик")
        {
            var catalog = Page("Товары", out var catalogBar);
            catalog.Controls.Add(tree);
            catalogBar.Controls.Add(Button("Добавить товар", () => EditProduct(true)));
            catalogBar.Controls.Add(Button("Изменить товар", () => EditProduct(false)));
            catalogBar.Controls.Add(Button("Все категории", () => { tree.SelectedNode = null; FillProducts(); }));
            tree.AfterSelect += (_, _) => FillProducts();
            Page("Категории", out var categoryBar);
            categoryBar.Controls.Add(Button("Добавить", () => EditCategory(true)));
            categoryBar.Controls.Add(Button("Изменить", () => EditCategory(false)));
        }
        if (Role is "Менеджер по заказам" or "Менеджер по закупкам")
        {
            var partners = Page("Контрагенты", out var partnerBar);
            partners.Text = Role == "Менеджер по заказам" ? "Клиенты" : "Поставщики";
            partnerBar.Controls.Add(Button("Добавить", () => EditPartner(true)));
            partnerBar.Controls.Add(Button("Изменить", () => EditPartner(false)));
            partnerBar.Controls.Add(Button("Удалить / архивировать", () =>
            {
                Need("Менеджер по закупкам", "Менеджер по заказам");
                var partner = D.Partners.First(x => x.Id == Selected("Контрагенты"));
                Need(partner.Supplier ? "Менеджер по закупкам" : "Менеджер по заказам");
                if (MessageBox.Show("Неиспользуемая запись будет удалена, используемая — переведена в архив.", "Подтверждение", MessageBoxButtons.OKCancel) == DialogResult.OK)
                { service.DeletePartner(partner.Id); Reload(); }
            }));
        }
        var orders = Page("Заказы и закупки", out var orderBar);
        orders.Text = Role switch { "Менеджер по заказам" => "Заказы", "Менеджер по закупкам" => "Закупки", "Курьер" => "Доставка", _ => "Заказы и закупки" };
        orderBar.Height = 100;
        orderBar.Controls.Add(search); search.TextChanged += (_, _) => FillOrders();
        if (Role == "Менеджер по заказам") orderBar.Controls.Add(Button("Новый заказ", () => EditOrder(true, false)));
        if (Role == "Менеджер по закупкам") orderBar.Controls.Add(Button("Новая закупка", () => EditOrder(true, true)));
        if (Role != "Курьер") orderBar.Controls.Add(Button(Role is "Менеджер по заказам" or "Менеджер по закупкам" ? "Состав / изменить" : "Просмотр состава", () => EditOrder(false, false)));
        if (Role == "Бухгалтер")
        {
            orderBar.Controls.Add(Button("Счёт", () => Document(false)));
            orderBar.Controls.Add(Button("Накладная", () => Document(true)));
            orderBar.Controls.Add(Button("Оплата", Pay));
        }
        if (Role == "Кладовщик")
        {
            orderBar.Controls.Add(Button("Сверить поставку", Receive));
            orderBar.Controls.Add(Button("Спецификация", () => ShowDocument("Спецификация на комплектацию", CurrentOrder())));
            orderBar.Controls.Add(Button("Отгрузить", () =>
            {
                Need("Кладовщик");
                if (MessageBox.Show("Состав партии проверен и передан службе доставки? Остатки будут уменьшены.", "Отгрузка", MessageBoxButtons.YesNo) == DialogResult.Yes)
                { service.Ship(CurrentOrder().Id); Reload(); }
            }));
        }
        if (Role == "Курьер") orderBar.Controls.Add(Button("Доставка", Delivery));
        if (Role == "Кладовщик")
        {
            Page("Остатки", out var stockBar);
            stockBar.Controls.Add(new Label { AutoSize = true, Text = "Красным выделены просроченные партии. Приход — после сверки и оформления бухгалтером накладной.", Padding = new Padding(3, 8, 0, 0) });
            Page("Приёмки", out var receiptBar);
            receiptBar.Controls.Add(new Label { AutoSize = true, Text = "Сверенные позиции: ожидающие накладную ещё не включены в остатки. Накладная оформляется во вкладке заказов.", Padding = new Padding(3, 8, 0, 0) });
        }
        if (Role == "Менеджер по закупкам")
        {
            Page("Потребность", out var needBar);
            needBar.Controls.Add(new Label { AutoSize = true, Text = "Показаны товары с остатком до 5 единиц или недостатком для ещё не отгруженных заказов.", Padding = new Padding(3, 8, 0, 0) });
        }
        if (Role == "Бухгалтер")
        {
            Page("Платежи", out _);
            Page("Отчёты", out var reportBar);
            reportBar.Controls.Add(Button("Сформировать отчёт", Report));
        }
        timer.Tick += (_, _) => UpdateStatus(); timer.Start();
        FormClosed += (_, _) => timer.Dispose();
        Reload();
    }

    private Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 29 };
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (IOException e) { MessageBox.Show("Файл недоступен или занят. Повторите действие после обновления.\n" + e.Message, "Склад"); }
            catch (Exception e) { MessageBox.Show(e.Message, "Склад"); }
        };
        return button;
    }
    private void Need(params string[] roles)
    {
        if (!roles.Contains(Role)) throw new Exception("Выберите рабочую роль: " + string.Join(" или ", roles));
    }
    private TabPage Page(string title, out FlowLayoutPanel buttons)
    {
        var page = new TabPage(title);
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, RowHeadersVisible = false, BackgroundColor = Color.White };
        buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 43, Padding = new Padding(4), AutoScroll = true };
        page.Controls.Add(grid); page.Controls.Add(buttons); tabs.TabPages.Add(page); grids[title] = grid;
        return page;
    }
    private void ShowRows(string page, object? rows) { if (grids.TryGetValue(page, out var grid)) grid.DataSource = rows; }
    private int Selected(string page)
    {
        var row = grids[page].CurrentRow;
        if (row == null) throw new Exception("Сначала выберите строку.");
        return Convert.ToInt32(row.Cells[0].Value);
    }
    private Order CurrentOrder() => D.Orders.First(x => x.Id == Selected("Заказы и закупки"));
    private string PartnerName(int id) => D.Partners.First(x => x.Id == id).Name;
    private new string ProductName(int id) => D.Products.First(x => x.Id == id).Name;
    private void UpdateStatus() => status.Text = Role is "Кладовщик" or "Бухгалтер"
        ? $"Сегодня: {DateTime.Today:dd.MM.yyyy}     Стоимость товаров на складе: {D.Batches.Sum(x => x.Quantity * x.Price):N2}     Оценка по ценам закупки"
        : $"Сегодня: {DateTime.Today:dd.MM.yyyy}     Рабочее место: {Role}";

    private void Reload()
    {
        service.Refresh();
        tree.Nodes.Clear();
        void AddNodes(TreeNodeCollection nodes, int parent)
        {
            foreach (var c in D.Categories.Where(x => x.ParentId == parent).OrderBy(x => x.Name))
            { var node = nodes.Add(c.ToString()); node.Tag = c.Id; AddNodes(node.Nodes, c.Id); }
        }
        AddNodes(tree.Nodes, 0); tree.ExpandAll();
        FillProducts();
        ShowRows("Категории", D.Categories.OrderBy(x => x.Id).ToList());
        ShowRows("Контрагенты", D.Partners.Where(x => x.Supplier == (Role == "Менеджер по закупкам")).OrderBy(x => x.Id).ToList());
        FillOrders();
        ShowRows("Приёмки", D.Orders.Where(x => x.Purchase).SelectMany(o => o.Receipts.Select(r => new { Закупка = o.Id, Товар = ProductName(r.Batch.ProductId), Количество = r.Batch.Quantity, Цена = r.Batch.Price, Поступление = r.Batch.Date.ToString("dd.MM.yyyy"), ГоденДо = r.Batch.Expiry?.ToString("dd.MM.yyyy") ?? "Бессрочно", Кладовщик = r.Batch.Keeper, Накладная = r.WaybillNumber == 0 ? "Ожидает оформления" : $"{o.Id}/{r.WaybillNumber}", ДатаНакладной = r.WaybillDate?.ToString("dd.MM.yyyy") ?? "" })).ToList());
        ShowRows("Остатки", D.Batches.Where(x => x.Quantity > 0).Select(x => new { Код = x.Id, Товар = ProductName(x.ProductId), Поставщик = PartnerName(x.SupplierId), Поступление = x.Date.ToString("dd.MM.yyyy"), ГоденДо = x.Expiry?.ToString("dd.MM.yyyy") ?? "Бессрочно", Кладовщик = x.Keeper, Количество = x.Quantity, Цена = x.Price, Стоимость = x.Quantity * x.Price }).ToList());
        if (grids.TryGetValue("Остатки", out var stockGrid))
        foreach (DataGridViewRow row in stockGrid.Rows)
            if (D.Batches.First(x => x.Id == Convert.ToInt32(row.Cells[0].Value)).Expiry < DateTime.Today) row.DefaultCellStyle.BackColor = Color.MistyRose;
        ShowRows("Потребность", D.Products.Select(p => new { Код = p.Id, Товар = p.Name, Остаток = service.Stock(p.Id), Заказано = D.Orders.Where(o => !o.Purchase && o.Status == "Оформлен").SelectMany(o => o.Lines).Where(l => l.ProductId == p.Id).Sum(l => l.Quantity) }).Where(x => x.Остаток <= 5 || x.Остаток < x.Заказано).ToList());
        ShowRows("Платежи", D.Payments.Select(x => new { Документ = x.OrderId, Дата = x.Date, Сумма = x.Amount, Направление = D.Orders.First(o => o.Id == x.OrderId).Purchase ? "Поставщику" : "От клиента" }).ToList());
        ShowRows("Отчёты", null);
        UpdateStatus();
    }
    private void FillProducts()
    {
        var ids = new HashSet<int>();
        void Collect(int id) { ids.Add(id); foreach (var c in D.Categories.Where(x => x.ParentId == id)) Collect(c.Id); }
        if (tree.SelectedNode?.Tag is int id) Collect(id);
        ShowRows("Товары", D.Products.Where(x => ids.Count == 0 || ids.Contains(x.CategoryId)).OrderBy(x => x.Id).ToList());
    }
    private void FillOrders()
    {
        string query = search.Text.Trim();
        var orders = D.Orders.Where(order => Role switch
        {
            "Менеджер по заказам" => !order.Purchase,
            "Менеджер по закупкам" => order.Purchase,
            "Курьер" => !order.Purchase && order.Status is "Передан доставке" or "В пути" or "Расхождение",
            _ => true
        });
        ShowRows("Заказы и закупки", orders.Where(x => $"{x.Id} {x.Date:dd.MM.yyyy} {PartnerName(x.PartnerId)} {x.Status} {string.Join(" ", x.Lines.Select(l => ProductName(l.ProductId)))}".Contains(query, StringComparison.CurrentCultureIgnoreCase)).Select(x => new { Номер = x.Id, Тип = x.Purchase ? "Закупка" : "Заказ", Дата = x.Date.ToString("dd.MM.yyyy"), Контрагент = PartnerName(x.PartnerId), Сумма = x.Total, Оплачено = service.Paid(x), Счёт = x.Invoice, Накладная = x.Waybill, Состояние = x.Status, Примечание = x.Note }).ToList());
    }
    private bool EditObject(string title, object value)
    {
        using var form = new Form { Text = title, Width = 610, Height = 650, StartPosition = FormStartPosition.CenterParent };
        var properties = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = value, PropertySort = PropertySort.NoSort, ToolbarVisible = false };
        var ok = new Button { Text = "Сохранить", Dock = DockStyle.Bottom, Height = 38 };
        ok.Click += (_, _) => { form.ActiveControl = ok; form.DialogResult = DialogResult.OK; };
        form.Controls.Add(properties); form.Controls.Add(ok);
        return form.ShowDialog(this) == DialogResult.OK;
    }
    private void EditCategory(bool add)
    {
        Need("Кладовщик");
        var item = add ? new Category() : Storage.Copy(D.Categories.First(x => x.Id == Selected("Категории")));
        if (EditObject("Категория: коды родителей указаны в таблице", item)) { service.SaveCategory(item); Reload(); }
    }
    private void EditProduct(bool add)
    {
        Need("Кладовщик");
        var item = add ? new Product() : Storage.Copy(D.Products.First(x => x.Id == Selected("Товары")));
        if (EditObject("Карточка товара: код категории указан в дереве", item)) { service.SaveProduct(item); Reload(); }
    }
    private void EditPartner(bool add)
    {
        Need("Менеджер по заказам", "Менеджер по закупкам");
        var item = add ? new Partner { Supplier = Role == "Менеджер по закупкам" } : Storage.Copy(D.Partners.First(x => x.Id == Selected("Контрагенты")));
        Need(item.Supplier ? "Менеджер по закупкам" : "Менеджер по заказам");
        if (EditObject("Контрагент", item))
        { Need(item.Supplier ? "Менеджер по закупкам" : "Менеджер по заказам"); service.SavePartner(item); Reload(); }
    }

    private void EditOrder(bool add, bool purchase)
    {
        var order = add ? new Order { Purchase = purchase } : Storage.Copy(CurrentOrder());
        bool canEdit = Role == (order.Purchase ? "Менеджер по закупкам" : "Менеджер по заказам") && order.Status == "Оформлен";
        if (add) Need(order.Purchase ? "Менеджер по закупкам" : "Менеджер по заказам");
        using var form = new Form { Text = (order.Purchase ? "Закупка" : "Заказ") + " — состав", Width = 850, Height = 560, StartPosition = FormStartPosition.CenterParent };
        var partner = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList, DataSource = D.Partners.Where(x => x.Supplier == order.Purchase && (x.Active || x.Id == order.PartnerId)).ToList() };
        if (order.PartnerId != 0) partner.SelectedItem = D.Partners.First(x => x.Id == order.PartnerId);
        var date = new DateTimePicker { Width = 130, Format = DateTimePickerFormat.Short, Value = order.Date };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 }; top.Controls.Add(partner); top.Controls.Add(date);
        var table = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = canEdit, AllowUserToDeleteRows = canEdit, ReadOnly = !canEdit, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, DataSource = new BindingList<Line>(order.Lines) };
        table.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "Товар", DataPropertyName = "ProductId", DataSource = D.Products.ToList(), DisplayMember = "Name", ValueMember = "Id" });
        foreach (var col in new[] { ("Quantity", "Количество"), ("Price", "Цена"), ("Total", "Сумма"), ("Received", "Сверено кладовщиком") })
            table.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = col.Item1, HeaderText = col.Item2, ReadOnly = col.Item1 == "Total" || col.Item1 == "Received" });
        table.DefaultValuesNeeded += (_, e) => { if (D.Products.Count > 0) { e.Row.Cells[0].Value = D.Products[0].Id; e.Row.Cells[1].Value = 1m; e.Row.Cells[2].Value = D.Products[0].Price; } };
        table.DataError += (_, e) => { e.ThrowException = false; MessageBox.Show("Выберите товар и введите числовое количество или цену."); };
        table.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 0 && table.Rows[e.RowIndex].Cells[0].Value is int id)
                table.Rows[e.RowIndex].Cells[2].Value = D.Products.First(x => x.Id == id).Price;
        };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 75 };
        bottom.Controls.Add(new Label { AutoSize = true, Text = "Новая позиция — пустая строка снизу. Удаление — выделить строку и нажать Delete.\nЦена фиксируется в документе; её можно изменить до сохранения." });
        var save = Button("Сохранить", () =>
        {
            if (!table.EndEdit()) throw new Exception("Исправьте введённые значения.");
            ((CurrencyManager)form.BindingContext![table.DataSource!]!).EndCurrentEdit();
            order.PartnerId = (partner.SelectedItem as Partner)?.Id ?? 0; order.Date = date.Value.Date;
            service.SaveOrder(order); form.DialogResult = DialogResult.OK;
        });
        save.Enabled = canEdit; partner.Enabled = canEdit; date.Enabled = canEdit; bottom.Controls.Add(save);
        form.Controls.Add(table); form.Controls.Add(top); form.Controls.Add(bottom);
        if (form.ShowDialog(this) == DialogResult.OK) Reload();
    }

    private void Document(bool waybill)
    {
        Need("Бухгалтер"); var order = CurrentOrder();
        if (waybill && order.Purchase)
        {
            var pending = order.Receipts.Where(x => x.WaybillNumber == 0).ToList();
            if (pending.Count > 0)
            {
                string details = string.Join("\n", pending.Select(x => $"{ProductName(x.Batch.ProductId)}: {x.Batch.Quantity} × {x.Batch.Price:F2}"));
                if (MessageBox.Show("Оформить приходную накладную по сверенным позициям?\n" + details + "\nПосле оформления эти партии будут оприходованы.", "Приходная накладная", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            }
        }
        service.MakeDocument(order.Id, waybill);
        order = D.Orders.First(x => x.Id == order.Id);
        Reload();
        if (waybill && order.Purchase)
        {
            int number = order.Receipts.Max(x => x.WaybillNumber);
            var receipts = order.Receipts.Where(x => x.WaybillNumber == number).ToList();
            var actual = Storage.Copy(order);
            actual.Date = receipts[0].WaybillDate!.Value;
            actual.Lines = receipts.Select(x => new Line { ProductId = x.Batch.ProductId, Quantity = x.Batch.Quantity, Price = x.Batch.Price }).ToList();
            ShowDocument("Приходная накладная", actual, $"{order.Id}/{number}");
            return;
        }
        ShowDocument(waybill ? (order.Purchase ? "Приходная накладная" : "Расходная накладная") : (order.Purchase ? "Счёт поставщика" : "Счёт клиенту"), order); Reload();
    }
    private void ShowDocument(string title, Order order, string? number = null)
    {
        var rows = new List<string[]> { new[] { "Товар", "Единица", "Количество", "Цена", "Сумма" } };
        rows.AddRange(order.Lines.Select(l => new[] { ProductName(l.ProductId), D.Products.First(p => p.Id == l.ProductId).Unit, l.Quantity.ToString(), l.Price.ToString("F2"), l.Total.ToString("F2") }));
        rows.Add(new[] { "Итого", "", "", "", order.Total.ToString("F2") });
        var p = D.Partners.First(x => x.Id == order.PartnerId);
        string heading = $"{title} № {number ?? order.Id.ToString()} от {order.Date:dd.MM.yyyy}\n{p.Name}, {p.Address}, {p.Country}\nСчёт {p.Account}; банк {p.Bank}; телефон {p.Phone}\nСостояние закупки/заказа: {order.Status}. Оплачено по закупке/заказу: {service.Paid(order):F2}";
        Export(heading, rows);
    }
    private void Pay()
    {
        Need("Бухгалтер"); var order = CurrentOrder();
        var input = new PaymentInput { Amount = order.Total - service.Paid(order) };
        if (EditObject("Регистрация фактически проведённой оплаты", input)) { service.Pay(order.Id, input.Amount); Reload(); }
    }
    private void Receive()
    {
        Need("Кладовщик"); var order = CurrentOrder();
        if (!order.Purchase) throw new Exception("Выберите закупку.");
        var line = order.Lines.FirstOrDefault(x => x.Received < x.Quantity) ?? throw new Exception("Закупка уже принята полностью.");
        var product = D.Products.First(x => x.Id == line.ProductId);
        var input = new ReceiptInput { ProductId = line.ProductId, Quantity = line.Quantity - line.Received, Keeper = D.Categories.First(x => x.Id == product.CategoryId).Keeper };
        if (EditObject("Сверка фактической поставки: остатки изменятся после накладной", input))
        { service.Receive(order.Id, input.ProductId, input.Quantity, input.Date, input.Expiry, input.Keeper); Reload(); }
    }
    private void Delivery()
    {
        Need("Курьер"); var order = CurrentOrder(); var input = new DeliveryInput();
        if (EditObject("Проверка партии и результат доставки", input))
        { service.Delivery(order.Id, input.Result == DeliveryResult.Принята ? "В пути" : input.Result == DeliveryResult.Доставлена ? "Доставлен" : "Расхождение", input.Note); Reload(); }
    }

    private void Report()
    {
        Need("Бухгалтер"); var input = new ReportInput();
        if (!EditObject("Отчёт по фактическим движениям товаров", input)) return;
        if (input.From.Date > input.To.Date) throw new Exception("Начало периода позже окончания.");
        var movements = D.Movements.Where(x => x.Date.Date >= input.From.Date && x.Date.Date <= input.To.Date && x.Incoming == (input.Kind == ReportKind.Закупленные) && (input.PartnerId == 0 || x.PartnerId == input.PartnerId) && (string.IsNullOrWhiteSpace(input.Country) || x.Country.Equals(input.Country.Trim(), StringComparison.CurrentCultureIgnoreCase))).ToList();
        var table = new DataTable();
        foreach (var col in new[] { "Дата", "Документ", "Товар", "Страна", "Контрагент", "Единица", "Количество", "Сумма" }) table.Columns.Add(col);
        foreach (var group in movements.GroupBy(x => new { x.ProductId, x.ProductName, x.Unit }))
        {
            foreach (var m in group.OrderBy(x => x.Date)) table.Rows.Add(m.Date.ToString("dd.MM.yyyy"), m.OrderId, m.ProductName, m.Country, PartnerName(m.PartnerId), m.Unit, m.Quantity, (m.Quantity * m.Price).ToString("F2"));
            table.Rows.Add("", "", "Итого: " + group.Key.ProductName, "", "", group.Key.Unit, group.Sum(x => x.Quantity), group.Sum(x => x.Quantity * x.Price).ToString("F2"));
        }
        foreach (var unit in movements.GroupBy(x => x.Unit)) table.Rows.Add("", "", "Общее количество", "", "", unit.Key, unit.Sum(x => x.Quantity), "");
        table.Rows.Add("", "", "Общая стоимость", "", "", "", "", movements.Sum(x => x.Quantity * x.Price).ToString("F2"));
        ShowRows("Отчёты", table);
        var rows = new List<string[]> { table.Columns.Cast<DataColumn>().Select(x => x.ColumnName).ToArray() };
        rows.AddRange(table.Rows.Cast<DataRow>().Select(x => x.ItemArray.Select(v => v?.ToString() ?? "").ToArray()));
        Export($"{input.Kind}: {input.From:dd.MM.yyyy} — {input.To:dd.MM.yyyy}\nКонтрагент: {(input.PartnerId == 0 ? "Все" : PartnerName(input.PartnerId))}; страна: {(string.IsNullOrWhiteSpace(input.Country) ? "Все" : input.Country)}", rows);
    }
    private void Export(string title, List<string[]> rows)
    {
        using var dialog = new SaveFileDialog { Filter = "Документ Word (*.docx)|*.docx|Таблица Excel (*.xml)|*.xml", FileName = "Документ" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (dialog.FilterIndex == 1) Documents.Word(dialog.FileName, title, rows);
        else Documents.Excel(dialog.FileName, title, rows);
        MessageBox.Show("Сохранено: " + dialog.FileName);
    }
}

public class PaymentInput { [DisplayName("Сумма платежа")] public decimal Amount { get; set; } }
public class ReceiptInput
{
    [DisplayName("Код товара из закупки")] public int ProductId { get; set; }
    [DisplayName("Принимаемое количество")] public decimal Quantity { get; set; }
    [DisplayName("Дата поступления")] public DateTime Date { get; set; } = DateTime.Today;
    [DisplayName("Годен до (пусто — бессрочно)")] public DateTime? Expiry { get; set; }
    [DisplayName("Ответственный кладовщик")] public string Keeper { get; set; } = "";
}
public enum DeliveryResult { Принята, Расхождение, Доставлена }
public class DeliveryInput
{
    [DisplayName("Результат")] public DeliveryResult Result { get; set; }
    [DisplayName("Примечание / описание расхождения")] public string Note { get; set; } = "";
}
public enum ReportKind { Закупленные, Отгруженные }
public class ReportInput
{
    [DisplayName("Вид движения")] public ReportKind Kind { get; set; }
    [DisplayName("С даты")] public DateTime From { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [DisplayName("По дату")] public DateTime To { get; set; } = DateTime.Today;
    [DisplayName("Код контрагента (0 — все)")] public int PartnerId { get; set; }
    [DisplayName("Страна (пусто — все)")] public string Country { get; set; } = "";
}
