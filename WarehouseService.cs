namespace Warehouse;

// Здесь правила склада. Окна только получают ввод и вызывают эти методы.
public class WarehouseService
{
    public Database Data { get; private set; }
    private readonly Storage storage;
    public WarehouseService(Storage storage) { this.storage = storage; Data = storage.Load(); }
    public void Refresh() => Data = storage.Load();
    private void Change(Action<Database> action) { storage.Change(Data.Revision, action); Refresh(); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static int Next(IEnumerable<int> ids) => ids.DefaultIfEmpty(0).Max() + 1;
    public decimal Paid(Order order) => Data.Payments.Where(x => x.OrderId == order.Id).Sum(x => x.Amount);
    public decimal Stock(int productId) => Data.Batches.Where(x => x.ProductId == productId).Sum(x => x.Quantity);

    public void SaveCategory(Category item) => Change(d =>
    {
        Check(!string.IsNullOrWhiteSpace(item.Name), "Укажите название категории.");
        if (item.Id == 0) item.Id = Next(d.Categories.Select(x => x.Id));
        Check(item.ParentId == 0 || d.Categories.Any(x => x.Id == item.ParentId), "Родительская категория не найдена.");
        int parent = item.ParentId;
        while (parent != 0)
        {
            Check(parent != item.Id, "Категория не может быть собственным потомком.");
            parent = d.Categories.First(x => x.Id == parent).ParentId;
        }
        Check(!d.Products.Any(x => x.CategoryId == item.ParentId), "Нельзя добавить подкатегорию к категории с товарами.");
        d.Categories.RemoveAll(x => x.Id == item.Id); d.Categories.Add(item);
    });

    public void SaveProduct(Product item) => Change(d =>
    {
        Check(!string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Unit) && !string.IsNullOrWhiteSpace(item.Country), "Заполните название, единицу измерения и страну.");
        Check(d.Categories.Any(x => x.Id == item.CategoryId) && !d.Categories.Any(x => x.ParentId == item.CategoryId), "Товар должен относиться к конечной категории.");
        Check(item.Price > 0 && item.Weight >= 0 && (item.ShelfDays == null || item.ShelfDays > 0), "Проверьте цену, вес и срок хранения.");
        if (item.Id == 0) item.Id = Next(d.Products.Select(x => x.Id));
        d.Products.RemoveAll(x => x.Id == item.Id); d.Products.Add(item);
    });

    public void SavePartner(Partner item) => Change(d =>
    {
        Check(!string.IsNullOrWhiteSpace(item.Name), "Введите название контрагента.");
        var old = d.Partners.FirstOrDefault(x => x.Id == item.Id);
        Check(old == null || old.Supplier == item.Supplier || !d.Orders.Any(x => x.PartnerId == item.Id), "Нельзя менять тип контрагента, у которого есть документы.");
        if (item.Id == 0) item.Id = Next(d.Partners.Select(x => x.Id));
        d.Partners.RemoveAll(x => x.Id == item.Id); d.Partners.Add(item);
    });

    public void DeletePartner(int id) => Change(d =>
    {
        var item = d.Partners.First(x => x.Id == id);
        if (d.Orders.Any(x => x.PartnerId == id)) item.Active = false;
        else d.Partners.Remove(item);
    });

    public void SaveOrder(Order order) => Change(d =>
    {
        var old = d.Orders.FirstOrDefault(x => x.Id == order.Id);
        Check(old == null || (old.Status == "Оформлен" && old.Lines.All(x => x.Received == 0)), "После начала исполнения состав документа менять нельзя.");
        Check(d.Partners.Any(x => x.Id == order.PartnerId && x.Supplier == order.Purchase && x.Active), "Выберите активного клиента или поставщика нужного типа.");
        Check(order.Lines.Count > 0, "Добавьте хотя бы одну позицию.");
        Check(order.Date.Date <= DateTime.Today, "Дата документа не может быть в будущем.");
        Check(order.Lines.Select(x => x.ProductId).Distinct().Count() == order.Lines.Count, "Каждый товар добавляется одной строкой.");
        foreach (var line in order.Lines)
        {
            Check(d.Products.Any(x => x.Id == line.ProductId), "Несуществующий код товара.");
            Check(line.Quantity > 0 && line.Price > 0, "Количество и цена должны быть положительными.");
            line.Received = 0;
        }
        Check(order.Total >= d.Payments.Where(x => x.OrderId == order.Id).Sum(x => x.Amount), "Сумма меньше уже внесённой оплаты. Возвраты в этой версии не поддерживаются.");
        if (order.Id == 0) order.Id = Next(d.Orders.Select(x => x.Id));
        order.Invoice = false; order.Waybill = false;
        d.Orders.RemoveAll(x => x.Id == order.Id); d.Orders.Add(order);
    });

    public void MakeDocument(int id, bool waybill) => Change(d =>
    {
        var order = d.Orders.First(x => x.Id == id);
        if (waybill)
        {
            if (order.Purchase)
            {
                var pending = order.Receipts.Where(x => x.WaybillNumber == 0).ToList();
                // Повторное открытие готовой накладной не приходует товар снова.
                if (pending.Count == 0)
                {
                    Check(order.Receipts.Any(x => x.WaybillNumber > 0), "Сначала кладовщик должен принять и сверить фактическую поставку.");
                    return;
                }
                int number = Next(order.Receipts.Select(x => x.WaybillNumber));
                foreach (var receipt in pending)
                {
                    receipt.WaybillNumber = number;
                    receipt.WaybillDate = DateTime.Today;
                    var batch = Storage.Copy(receipt.Batch);
                    batch.Id = Next(d.Batches.Select(x => x.Id));
                    d.Batches.Add(batch);
                    AddMovement(d, order, batch, batch.Quantity, true, batch.Date);
                }
                order.Status = order.Lines.All(x => x.Received == x.Quantity) ? "Получен" : "Получен частично";
            }
            else Check(d.Payments.Where(x => x.OrderId == id).Sum(x => x.Amount) >= order.Total, "Расходная накладная доступна после полной оплаты.");
            order.Waybill = true;
        }
        else order.Invoice = true;
    });

    public void Pay(int id, decimal amount) => Change(d =>
    {
        var order = d.Orders.First(x => x.Id == id);
        Check(order.Invoice, "Сначала оформите счёт.");
        decimal paid = d.Payments.Where(x => x.OrderId == id).Sum(x => x.Amount);
        Check(amount > 0 && paid + amount <= order.Total, "Сумма должна быть больше нуля и не превышать долг.");
        d.Payments.Add(new Payment { OrderId = id, Amount = amount });
    });

    public void Receive(int id, int productId, decimal quantity, DateTime date, DateTime? expiry, string keeper) => Change(d =>
    {
        var order = d.Orders.First(x => x.Id == id);
        Check(order.Purchase, "Для приёмки выберите закупку.");
        var line = order.Lines.FirstOrDefault(x => x.ProductId == productId)
            ?? throw new Exception("Такого товара нет в выбранной закупке.");
        var product = d.Products.First(x => x.Id == productId);
        Check(quantity > 0 && quantity <= line.Quantity - line.Received, "Количество превышает ожидаемый остаток поставки или равно нулю.");
        Check(date.Date >= order.Date.Date && date.Date <= DateTime.Today, "Дата поступления должна быть между датой закупки и сегодняшней датой.");
        Check(product.ShelfDays == null || expiry != null, "Укажите фактическую дату окончания срока годности партии.");
        Check(!string.IsNullOrWhiteSpace(keeper), "Укажите ответственного кладовщика.");
        var batch = new Batch { ProductId = productId, SupplierId = order.PartnerId, Quantity = quantity, Price = line.Price, Date = date.Date, Expiry = expiry?.Date, Keeper = keeper };
        order.Receipts.Add(new Receipt { Batch = batch });
        line.Received += quantity;
        order.Status = "Ожидает приходную накладную";
    });

    private static void AddMovement(Database d, Order order, Batch batch, decimal quantity, bool incoming, DateTime date)
    {
        var product = d.Products.First(x => x.Id == batch.ProductId);
        d.Movements.Add(new Movement { Date = date, OrderId = order.Id, BatchId = batch.Id, ProductId = product.Id, ProductName = product.Name, Country = product.Country, Unit = product.Unit, PartnerId = order.PartnerId, Incoming = incoming, Quantity = quantity, Price = order.Lines.First(x => x.ProductId == product.Id).Price });
    }

    public void Ship(int id) => Change(d =>
    {
        var order = d.Orders.First(x => x.Id == id);
        Check(!order.Purchase && order.Status == "Оформлен", "Этот заказ уже отгружен или является закупкой.");
        Check(order.Waybill && d.Payments.Where(x => x.OrderId == id).Sum(x => x.Amount) >= order.Total, "Нужны полная оплата и расходная накладная.");
        foreach (var line in order.Lines)
            Check(d.Batches.Where(x => x.ProductId == line.ProductId).Sum(x => x.Quantity) >= line.Quantity, "Недостаточно товара: " + d.Products.First(x => x.Id == line.ProductId).Name);
        foreach (var line in order.Lines)
        {
            decimal remaining = line.Quantity;
            // Сначала расходуется самая ранняя поставка (FIFO).
            foreach (var batch in d.Batches.Where(x => x.ProductId == line.ProductId && x.Quantity > 0).OrderBy(x => x.Date).ThenBy(x => x.Id))
            {
                decimal count = Math.Min(remaining, batch.Quantity);
                if (count == 0) break;
                batch.Quantity -= count; remaining -= count;
                AddMovement(d, order, batch, count, false, DateTime.Now);
            }
        }
        order.Status = "Передан доставке";
    });

    public void Delivery(int id, string status, string note) => Change(d =>
    {
        var order = d.Orders.First(x => x.Id == id);
        Check(!order.Purchase && (order.Status == "Передан доставке" || order.Status == "Расхождение" || order.Status == "В пути"), "Заказ ещё не отгружен или уже доставлен.");
        Check(new[] { "В пути", "Расхождение", "Доставлен" }.Contains(status), "Неверный результат доставки.");
        Check(status != "Расхождение" || !string.IsNullOrWhiteSpace(note), "Опишите расхождение.");
        Check(status != "Доставлен" || order.Status == "В пути", "Сначала проверьте партию и примите её в доставку.");
        order.Status = status; order.Note = note;
    });

    public void Demo() => Change(d =>
    {
        Check(d.Products.Count == 0 && d.Partners.Count == 0 && d.Categories.Count == 0 && d.Orders.Count == 0, "Пример добавляется только в пустую базу.");
        d.Categories.AddRange(new[] { new Category { Id = 1, Name = "Промышленные товары" }, new Category { Id = 2, Name = "Бумага", ParentId = 1, Keeper = "Иванова" }, new Category { Id = 3, Name = "Продовольственные товары" }, new Category { Id = 4, Name = "Молочные", ParentId = 3, Keeper = "Петров" } });
        d.Products.AddRange(new[] { new Product { Id = 1, Name = "Бумага A4, 500 листов", CategoryId = 2, Price = 15, Country = "Беларусь", Weight = 2.5m, Package = "Пачка" }, new Product { Id = 2, Name = "Молоко 1 л", CategoryId = 4, Price = 3, Country = "Беларусь", ShelfDays = 7, Weight = 1, Package = "Пакет" } });
        d.Partners.AddRange(new[] { new Partner { Id = 1, Name = "ООО Поставка", Supplier = true, Country = "Беларусь", Phone = "+375 29 0000000" }, new Partner { Id = 2, Name = "ООО Покупатель", Country = "Беларусь" } });
    });
}
