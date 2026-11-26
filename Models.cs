using System.ComponentModel;

namespace Warehouse;

// DisplayName задаёт русские названия полей в редакторе и таблицах.
public class Category
{
    [ReadOnly(true)] public int Id { get; set; }
    [DisplayName("Название")] public string Name { get; set; } = "";
    [DisplayName("Код родителя (0 — корень)")] public int ParentId { get; set; }
    [DisplayName("Ответственный кладовщик")] public string Keeper { get; set; } = "";
    public override string ToString() => $"{Id}: {Name}";
}

public class Product
{
    [ReadOnly(true)] public int Id { get; set; }
    [DisplayName("Название")] public string Name { get; set; } = "";
    [DisplayName("Код конечной категории")] public int CategoryId { get; set; }
    [DisplayName("Единица измерения")] public string Unit { get; set; } = "шт";
    [DisplayName("Описание")] public string Description { get; set; } = "";
    [DisplayName("Вес, кг")] public decimal Weight { get; set; }
    [DisplayName("Упаковка")] public string Package { get; set; } = "";
    [DisplayName("Хранение, дней (пусто — бессрочно)")] public int? ShelfDays { get; set; }
    [DisplayName("Цена")] public decimal Price { get; set; }
    [DisplayName("Страна происхождения")] public string Country { get; set; } = "";
    public override string ToString() => $"{Id}: {Name}";
}

public class Partner
{
    [ReadOnly(true)] public int Id { get; set; }
    [DisplayName("Название")] public string Name { get; set; } = "";
    [DisplayName("Поставщик (иначе клиент)")] public bool Supplier { get; set; }
    [DisplayName("Активен")] public bool Active { get; set; } = true;
    [DisplayName("Страна")] public string Country { get; set; } = "";
    [DisplayName("Адрес")] public string Address { get; set; } = "";
    [DisplayName("Форма собственности")] public string Ownership { get; set; } = "";
    [DisplayName("Банковский счет")] public string Account { get; set; } = "";
    [DisplayName("Банк")] public string Bank { get; set; } = "";
    [DisplayName("Руководитель")] public string Director { get; set; } = "";
    [DisplayName("Телефон")] public string Phone { get; set; } = "";
    [DisplayName("Email")] public string Email { get; set; } = "";
    [DisplayName("Контактный менеджер")] public string Manager { get; set; } = "";
    public override string ToString() => $"{Id}: {Name}";
}

public class Line
{
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal Received { get; set; }
    public decimal Total => Quantity * Price;
}

public class Order
{
    public int Id { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public bool Purchase { get; set; }
    public int PartnerId { get; set; }
    public List<Line> Lines { get; set; } = new();
    public List<Receipt> Receipts { get; set; } = new();
    public bool Invoice { get; set; }
    public bool Waybill { get; set; }
    public string Status { get; set; } = "Оформлен";
    public string Note { get; set; } = "";
    public decimal Total => Lines.Sum(x => x.Total);
}

public class Payment
{
    public int OrderId { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public decimal Amount { get; set; }
}

// Результат сверки кладовщиком. До оформления накладной это ещё не остаток.
public class Receipt
{
    public Batch Batch { get; set; } = new();
    public int WaybillNumber { get; set; } // 0 — ожидает оформления бухгалтером
    public DateTime? WaybillDate { get; set; }
}

public class Batch
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int SupplierId { get; set; }
    public DateTime Date { get; set; }
    public DateTime? Expiry { get; set; }
    public string Keeper { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
}

public class Movement
{
    public DateTime Date { get; set; } = DateTime.Now;
    public int OrderId { get; set; }
    public int BatchId { get; set; }
    public int ProductId { get; set; }
    public int PartnerId { get; set; }
    public bool Incoming { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    // Снимки названия, страны и единицы сохраняют смысл старых отчётов.
    public string ProductName { get; set; } = "";
    public string Country { get; set; } = "";
    public string Unit { get; set; } = "";
}

public class Database
{
    public int Revision { get; set; }
    public List<Category> Categories { get; set; } = new();
    public List<Product> Products { get; set; } = new();
    public List<Partner> Partners { get; set; } = new();
    public List<Order> Orders { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
    public List<Batch> Batches { get; set; } = new();
    public List<Movement> Movements { get; set; } = new();
}
