namespace RioCharts.Docs;

/// <summary>
/// Deterministic sample data for the examples: a small online store selling outdoor gear. Its traffic,
/// orders, revenue, stock and servers. Money in euros.
/// </summary>
public static class SampleData
{
    public sealed record Reading(DateTime Time, double? Value);

    public sealed record Sales(string Quarter, double LastYear, double ThisYear);

    public sealed record Share(string Name, double Value);

    /// <summary>One day of advertising: spend and the revenue it brought, per channel.</summary>
    public sealed record AdDay(DateTime Date, double SearchSpend, double SearchRevenue, double DisplaySpend, double DisplayRevenue);

    /// <summary>One delivery: distance and how long it took.</summary>
    public sealed record Delivery(double Km, double Hours);

    /// <summary>Revenue per product group and month, k€.</summary>
    public sealed record Monthly(string Month, double Shoes, double Clothing, double Gear);

    /// <summary>A support ticket: its topic, subject and handling time.</summary>
    public sealed record Ticket(string Topic, string Subject, double Minutes);

    public static readonly DateTime Start = new(2026, 11, 16);

    /// <summary>The daily traffic pattern: low at night, a peak in the evening (around 20:00), in [-1, 1].</summary>
    private static double Cycle(DateTime t) => Math.Sin((t.Hour + t.Minute / 60.0 - 14) / 24 * 2 * Math.PI);

    private static List<Reading> Series(Func<int, DateTime, double?> value, int count = 2 * 96, int minutes = 15) =>
        Enumerable.Range(0, count).Select(i => Start.AddMinutes(minutes * i)).Select((t, i) => new Reading(t, value(i, t))).ToList();

    /// <summary>Sessions per 15 minutes on the website, two days.</summary>
    /// <summary>Checkout response time in ms every 15 minutes: slower when busy, with a few spikes.</summary>
    public static readonly List<Reading> ResponseMs = Series((i, t) =>
        Math.Round(72 + 58 * Math.Max(0, Cycle(t)) + Drift(i, 31) * 14 + (Noise(i, 32) > 0.485 ? 45 : 0) + Noise(i, 33) * 6));

    public static readonly List<Reading> WebSessions = Series((i, t) => Math.Round(Math.Max(5, 140 + 110 * Cycle(t) + Drift(i, 1) * 40)));

    /// <summary>Sessions per 15 minutes in the app, two days.</summary>
    public static readonly List<Reading> AppSessions = Series((i, t) => Math.Round(Math.Max(5, 90 + 70 * Cycle(t) + Drift(i, 2) * 30)));

    /// <summary>Sessions per 15 minutes from search, social and email (they add up), two days.</summary>
    public static readonly List<Reading> Search = Series((i, t) => Math.Round(Math.Max(3, 80 + 60 * Cycle(t) + Drift(i, 4) * 20)));

    public static readonly List<Reading> Social = Series((i, t) => Math.Round(Math.Max(2, 45 + 40 * Cycle(t) + Drift(i, 5) * 15)));

    public static readonly List<Reading> Email = Series((i, t) => Math.Round(t.Hour is >= 7 and < 10 ? 50 + Drift(i, 6) * 20 : 10 + Drift(i, 6) * 6));

    /// <summary>Conversion rate in %, with a gap while tracking was down.</summary>
    public static readonly List<Reading> Conversion = Series((i, t) => i is >= 60 and < 75 ? null : Math.Round(2.6 + 0.7 * Cycle(t) + Drift(i, 3, 6) * 0.6, 2));

    /// <summary>Revenue per 15 minutes, €.</summary>
    public static readonly List<Reading> Revenue = Series((i, t) => Math.Round(Math.Max(0, 900 + 750 * Cycle(t) + Drift(i, 7) * 300)));

    /// <summary>Orders per day, the last 35 days.</summary>
    public static readonly List<Reading> DailyOrders = Enumerable.Range(0, 35)
        .Select(d => Start.AddDays(d - 34))
        .Select((d, i) => new Reading(d, Math.Round(220 + 60 * Math.Sin(d.DayOfYear / 5.0) + Noise(i, 7) * 25, 0)))
        .ToList();

    /// <summary>Server capacity in sessions per 15 minutes: scaled up for the day, down at night (a step line).</summary>
    public static readonly List<Reading> Capacity =
    [
        new(Start, 200), new(Start.AddHours(8), 300), new(Start.AddHours(16), 400), new(Start.AddHours(23), 200),
        new(Start.AddHours(32), 300), new(Start.AddHours(40), 400), new(Start.AddHours(47), 200), new(Start.AddHours(48), 200),
    ];

    /// <summary>The reorder level for a product, changed at irregular times (a campaign, then back).</summary>
    public static readonly List<Reading> ReorderLevel =
    [
        new(Start, 40), new(Start.AddHours(6.5), 60), new(Start.AddHours(9.75), 80), new(Start.AddHours(13.2), 60),
        new(Start.AddHours(17.6), 50), new(Start.AddHours(22.4), 40), new(Start.AddHours(24), 40),
    ];

    /// <summary>
    /// The product's stock, logged on change: a reading when it has moved 3 items since the last one,
    /// or after an hour without one. Dense while it sells fast and when a delivery comes in, sparse at
    /// night, and nothing for two hours while the warehouse system was offline.
    /// </summary>
    public static readonly List<Reading> StockOnChange = LogOnChange();

    private static List<Reading> LogOnChange()
    {
        var readings = new List<Reading>();
        double stock = 120, logged = double.NaN;
        var last = DateTime.MinValue;
        for (var minute = 0; minute <= 24 * 60; minute++)
        {
            var t = Start.AddMinutes(minute);
            var hour = minute / 60.0;
            var rate = hour is >= 8 and < 23 ? 0.12 + 0.08 * Math.Sin((hour - 8) / 15 * Math.PI) : 0.01; // items sold per minute
            stock = Math.Max(0, stock - rate * (1 + Drift(minute, 7, 30)));
            var reorder = ReorderLevel.Last(r => r.Time <= t).Value!.Value;
            if (stock < reorder && minute % 180 == 0)
            {
                stock += 60; // a delivery
            }

            var offline = minute is >= 13 * 60 + 10 and < 15 * 60 + 5;
            if (offline)
            {
                continue;
            }

            if (double.IsNaN(logged) || Math.Abs(stock - logged) >= 3 || (t - last).TotalMinutes >= 60)
            {
                logged = Math.Round(stock);
                last = t;
                readings.Add(new Reading(t, logged));
            }
        }

        return readings;
    }

    /// <summary>Sales per hour of five products over two days (one keeps growing; one sold out for a while).</summary>
    public static readonly List<(string Product, List<Reading> Readings)> ProductSales =
    [
        ("Trail shoe · Ridge 2", Series((i, t) => Math.Round(Math.Max(0, 14 + 10 * Cycle(t) + Drift(i, 11) * 5), 1), 48, 60)),
        ("Rain jacket · Storm", Series((i, t) => Math.Round(Math.Max(0, 9 + 6 * Cycle(t) + Drift(i, 12) * 4), 1), 48, 60)),
        ("Headlamp · Beam 400", Series((i, t) => Math.Round(Math.Max(0, 6 + (t.Hour is >= 18 and < 23 ? 6 : 0) + Drift(i, 13, 4) * 3), 1), 48, 60)),
        ("Water bottle · 1 l", Series((i, t) => Math.Round(Math.Max(0, 4 + 0.35 * i + Drift(i, 14) * 3), 1), 48, 60)),
        ("Tent · Dome 2p", Series((i, t) => i is >= 30 and < 36 ? null : Math.Round(Math.Max(0, 3 + 2 * Cycle(t) + Drift(i, 15) * 2), 1), 48, 60)),
    ];

    /// <summary>Sessions in one hour of the day ("00" to "23").</summary>
    public sealed record HourCount(string Hour, double Sessions);

    /// <summary>
    /// Sessions per weekday and hour, averaged over the last month: quiet nights, a lunch bump, the
    /// evening peak, and more traffic at the weekend.
    /// </summary>
    public static readonly List<(string Day, List<HourCount> Hours)> WeekTraffic =
        new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" }.Select((day, d) => (day, Enumerable.Range(0, 24).Select(h =>
        {
            var weekend = d >= 5;
            var daytime = Math.Max(0, Math.Sin((h - 6) / 17.0 * Math.PI));
            var value = 12 + (weekend ? 230 : 170) * daytime + (h is 12 or 13 ? 35 : 0) + (h is >= 19 and <= 22 ? (weekend ? 60 : 95) : 0) + Noise(d * 24 + h, 51) * 25;
            return new HourCount(h.ToString("00"), Math.Round(Math.Max(4, value)));
        }).ToList())).ToList();

    /// <summary>Conversion rate in % per sales channel and weekday.</summary>
    public static readonly List<(string Channel, List<Share> Days)> ChannelConversion =
        new[] { ("Web", 2.4), ("App", 3.6), ("Marketplace", 1.5) }.Select((c, k) => (c.Item1,
            new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" }.Select((day, d) =>
                new Share(day, Math.Round(c.Item2 + (d >= 5 ? 0.6 : 0) + Noise(k * 7 + d, 52) * 0.8, 1))).ToList())).ToList();

    /// <summary>A day of revenue: the lowest, average and highest of the same date over the last three years, €.</summary>
    public sealed record DayRange(DateTime Date, double Low, double Average, double High);

    /// <summary>Revenue per day for six weeks, as the range and average of the last three years (weekly rhythm, slow growth).</summary>
    public static readonly List<DayRange> RevenueHistory = Enumerable.Range(0, 42).Select(d =>
    {
        var date = Start.AddDays(d - 42);
        var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var average = Math.Round(9000 + (weekend ? 3200 : 0) + d * 45 + Drift(d, 61, 3) * 1200);
        return new DayRange(date, Math.Round(average - 1400 - (Noise(d, 62) + 0.5) * 1600), average, Math.Round(average + 1300 + (Noise(d, 63) + 0.5) * 1800));
    }).ToList();

    /// <summary>
    /// Outdoor temperature in °C at a weather station every 15 minutes for three weeks: warm
    /// afternoons, cool nights, a slow drift, and a thunderstorm on the tenth afternoon.
    /// </summary>
    public static readonly List<Reading> Outdoor = Enumerable.Range(0, 21 * 96).Select(i =>
    {
        var t = Start.AddDays(-21).AddMinutes(15 * i);
        var day = Math.Sin((t.Hour + t.Minute / 60.0 - 9) / 24 * 2 * Math.PI) * 4.5;
        var storm = i / 96 == 9 && t.Hour is >= 13 and < 17 ? -7 * Math.Sin((t.Hour - 13 + t.Minute / 60.0) / 4 * Math.PI) : 0;
        return new Reading(t, Math.Round(14 + day + Drift(i, 81, 24) * 3 + Noise(i, 82) * 0.6 + storm, 1));
    }).ToList();

    /// <summary>A day of electricity prices, €/MWh: the first and last hour, the highest and the lowest.</summary>
    public sealed record PriceDay(DateTime Date, double First, double Highest, double Lowest, double Last);

    public static readonly List<PriceDay> Prices = Enumerable.Range(0, 30).Select(d =>
    {
        var date = Start.AddDays(d - 30);
        var level = 62 + Math.Sin(d / 4.5) * 14 + (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? -9 : 0);
        var first = Math.Round(level + Noise(d, 91) * 26, 1);
        var last = Math.Round(level + Noise(d, 92) * 26, 1);
        return new PriceDay(date, first, Math.Round(Math.Max(first, last) + 3 + (Noise(d, 93) + 0.5) * 14, 1), Math.Round(Math.Min(first, last) - 2 - (Noise(d, 94) + 0.5) * 8, 1), last);
    }).ToList();

    /// <summary>One test score (0-100, higher is better) of two laptops and the class average.</summary>
    public sealed record LaptopScore(string Measure, double Laptop, double OldLaptop, double Average);

    /// <summary>Two laptops' review scores against the average of their class; the older one's display was not tested.</summary>
    public static readonly List<LaptopScore> LaptopScores =
    [
        new("Performance", 82, 60, 75),
        new("Battery", 64, 48, 75),
        new("Display", 71, double.NaN, 75),
        new("Keyboard", 90, 70, 75),
        new("Weight", 55, 40, 75),
        new("Price", 77, 52, 75),
    ];

    /// <summary>A website's average visits per hour of the day, every other hour.</summary>
    public sealed record HourVisits(string Hour, double Weekday, double Weekend);

    public static readonly List<HourVisits> HourlyVisits = Enumerable.Range(0, 12).Select(i =>
    {
        var h = i * 2;
        var work = h is >= 8 and <= 16 ? 380 : 0;
        return new HourVisits(h.ToString("00") + ":00", Math.Round(420 + work + Math.Sin((h - 9) / 24.0 * 2 * Math.PI) * 300),
            Math.Round(520 + Math.Sin((h - 14) / 24.0 * 2 * Math.PI) * 330));
    }).ToList();

    /// <summary>A country's population in millions (rounded) and its continent.</summary>
    public sealed record Country(string Continent, string Name, double Millions);

    public static readonly List<Country> Countries =
    [
        new("Asia", "India", 1430), new("Asia", "China", 1410), new("Asia", "Indonesia", 278), new("Asia", "Pakistan", 240),
        new("Asia", "Bangladesh", 173), new("Asia", "Japan", 124), new("Asia", "Philippines", 117), new("Asia", "Vietnam", 99),
        new("Asia", "Thailand", 72), new("Asia", "Nepal", 31), new("Asia", "Laos", 8),
        new("Africa", "Nigeria", 224), new("Africa", "Ethiopia", 127), new("Africa", "Egypt", 113), new("Africa", "DR Congo", 102),
        new("Africa", "Tanzania", 67), new("Africa", "South Africa", 60), new("Africa", "Kenya", 55), new("Africa", "Ghana", 34),
        new("Europe", "Russia", 144), new("Europe", "Germany", 84), new("Europe", "United Kingdom", 68), new("Europe", "France", 68),
        new("Europe", "Italy", 59), new("Europe", "Spain", 48), new("Europe", "Poland", 37), new("Europe", "Sweden", 10.5),
        new("Europe", "Norway", 5.5), new("Europe", "Denmark", 5.9), new("Europe", "Finland", 5.6),
        new("Americas", "USA", 335), new("Americas", "Brazil", 216), new("Americas", "Mexico", 129), new("Americas", "Colombia", 52),
        new("Americas", "Argentina", 46), new("Americas", "Canada", 40), new("Americas", "Peru", 34),
        new("Oceania", "Australia", 27), new("Oceania", "Papua New Guinea", 10), new("Oceania", "New Zealand", 5.2),
    ];

    /// <summary>A folder on a disk, its parent folder and the gigabytes of the files directly in it.</summary>
    public sealed record Folder(string Name, string? Parent, double? Gigabytes);

    public static readonly List<Folder> Folders =
    [
        new("Home", null, null), new("Photos", "Home", null), new("2024", "Photos", 96), new("2025", "Photos", 132),
        new("Videos", "Home", 210), new("Music", "Home", 48), new("Documents", "Home", 12),
        new("Work", null, null), new("Projects", "Work", 74), new("Archive", "Work", 160), new("Mail", "Work", 18),
        new("System", null, null), new("Apps", "System", 85), new("Cache", "System", 22), new("Updates", "System", 9),
    ];

    /// <summary>Orders per day of a week, against a target of 100 a day.</summary>
    public sealed record DayOrders(string Day, double Orders);

    public static readonly List<DayOrders> WeekOrders =
    [
        new("Mon", 132), new("Tue", 118), new("Wed", 96), new("Thu", 141), new("Fri", 88), new("Sat", 156), new("Sun", 104),
    ];

    /// <summary>One order: the weekday, the channel and its value in €.</summary>
    public sealed record OrderValue(string Day, string Channel, double Value);

    /// <summary>
    /// Last month's orders, one row each: values spread wide (most small, a few large), a little
    /// higher at the weekend, and the app's a bit higher than the web's.
    /// </summary>
    public static readonly List<OrderValue> OrderValues = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" }
        .SelectMany((day, d) => new[] { ("Web", 0.0), ("App", 0.12) }.SelectMany(c => Enumerable.Range(0, 60).Select(i =>
        {
            var weekend = d >= 5 ? 0.15 : 0;
            var spread = Noise(d * 1000 + i * 7 + (c.Item1 == "App" ? 500 : 0), 71) + Noise(i * 13 + d, 72) * 0.6;
            var big = Noise(i * 31 + d * 17, 73) > 0.46 ? 2.2 : 1; // now and then a large order
            return new OrderValue(day, c.Item1, Math.Round(Math.Exp(4.1 + weekend + c.Item2 + spread * 0.9) * big, 2));
        })))
        .ToList();

    /// <summary>Response time percentiles per API endpoint, ms, as a monitoring tool reports them.</summary>
    public sealed record Percentiles(string Endpoint, double P5, double P25, double P50, double P75, double P95);

    public static readonly List<Percentiles> ResponseTimes =
    [
        new("/products", 38, 52, 61, 74, 112), new("/search", 71, 95, 118, 156, 260), new("/cart", 22, 29, 34, 41, 63),
        new("/checkout", 140, 182, 215, 268, 410), new("/account", 30, 41, 47, 58, 90),
    ];

    public static readonly List<Sales> Quarters =
    [
        new("Q1", 182_000, 214_000), new("Q2", 236_000, 251_000), new("Q3", 198_000, 243_000), new("Q4", 271_000, 305_000),
    ];

    public static readonly List<Monthly> Mix =
    [
        new("Jan", 42, 18, 6), new("Feb", 38, 17, 7), new("Mar", 29, 21, 11), new("Apr", 33, 24, 19), new("May", 36, 26, 28), new("Jun", 31, 22, 35),
    ];

    /// <summary>Revenue per category this year, k€.</summary>
    public static readonly List<Share> CategoryRevenue =
    [
        new("Shoes", 412), new("Clothing", 236), new("Camping", 148), new("Accessories", 97), new("Bags", 31), new("Gift cards", 9), new("Socks", 6),
    ];

    // One row per ticket, so topics repeat: aggregate them per topic.
    public static readonly List<Ticket> Tickets =
    [
        new("Shipping", "Where is my order?", 45), new("Payment", "Card declined", 120), new("Shipping", "Wrong address", 15),
        new("Returns", "Wrong size", 30), new("Shipping", "Late delivery", 60), new("Payment", "Double charge", 90),
        new("Account", "Password reset", 20), new("Shipping", "Damaged parcel", 35), new("Returns", "Refund status", 75),
        new("Payment", "Invoice", 40),
    ];

    /// <summary>This month's goals: % reached.</summary>
    public static readonly List<Share> Goals = [new("Revenue", 87), new("Orders", 74), new("New customers", 41), new("Reviews", 92)];

    /// <summary>Orders per hour, UTC, across the end of summer time in Europe (25 Oct 2026).</summary>
    public static readonly (DateTimeOffset Time, double Value)[] DstOrders = Enumerable.Range(0, 36)
        .Select(h => (new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero).AddHours(h), Math.Round(Math.Max(2, 40 + 30 * Math.Sin((h - 8) / 24.0 * 2 * Math.PI)))))
        .ToArray();

    /// <summary>
    /// Thirty days of ads, daily. Search ads pay off (revenue grows with spend); display ads do not
    /// (revenue stays about the same whatever is spent).
    /// </summary>
    public static readonly List<AdDay> Ads = Enumerable.Range(0, 30).Select(i =>
    {
        var search = Math.Round(200 + 600 * (Noise(i, 31) + 0.5), 0);
        var display = Math.Round(150 + 500 * (Noise(i, 32) + 0.5), 0);
        return new AdDay(Start.AddDays(i - 30), search, Math.Round(800 + 3.1 * search + Noise(i, 33) * 500), display,
            Math.Round(1400 + 0.2 * display + Noise(i, 34) * 500));
    }).ToList();

    /// <summary>300 deliveries: a day in town, a day or two further away, three to four days to remote places.</summary>
    public static readonly List<Delivery> Deliveries = Enumerable.Range(0, 300).Select(i =>
    {
        var km = Math.Round(Math.Pow(Noise(i, 41) + 0.5, 2) * 900 + 2, 0);
        return new Delivery(km, Math.Round(Math.Max(4, 18 + km * 0.09 + Noise(i, 42) * 14), 1));
    }).ToList();

    /// <summary>One step of a load test: concurrent users and the response times measured at that load.</summary>
    public sealed record LoadStep(double Users, double MedianMs, double P95Ms);

    /// <summary>A load test from 10 to 500 concurrent users: flat at first, then a knee as the servers saturate.</summary>
    public static readonly List<LoadStep> LoadTest = Enumerable.Range(0, 50).Select(i =>
    {
        var users = 10.0 * (i + 1);
        var median = 38 * (1 + 0.9 * Math.Pow(users / 420, 4)) + Noise(i, 51) * 3;
        var p95 = 65 * (1 + 1.2 * Math.Pow(users / 380, 4)) + Noise(i, 52) * 6;
        return new LoadStep(users, Math.Round(median, 1), Math.Round(p95, 1));
    }).ToList();

    /// <summary>Server response times in ms, a random walk as plain arrays (the columnar fast path).</summary>
    /// <summary>Orders: minutes on the site before buying, and the order value in €. Most orders are quick and small.</summary>
    public static (double[] Minutes, double[] Values) Orders(int count)
    {
        var random = new Random(42);
        double Gauss() => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
        var minutes = new double[count];
        var values = new double[count];
        for (var i = 0; i < count; i++)
        {
            minutes[i] = Math.Round(Math.Exp(2.2 + 0.55 * Gauss()), 2);
            values[i] = Math.Round(Math.Max(5, 18 + minutes[i] * 2.4 + 22 * Gauss() * Math.Sqrt(minutes[i] / 10)), 2);
        }

        return (minutes, values);
    }

    public static (DateTime[] Times, double[] Values) Big(int count)
    {
        var times = new DateTime[count];
        var values = new double[count];
        var value = 180.0;
        for (var i = 0; i < count; i++)
        {
            times[i] = Start.AddDays(-30).AddSeconds(i * 13);
            value = Math.Max(40, value + Noise(i, 9) * 4);
            values[i] = value;
        }

        return (times, values);
    }

    /// <summary>
    /// Repeatable, smooth noise in [-0.5, 0.5]: <see cref="Noise"/> at every <paramref name="span"/>th sample, eased between.
    /// Per-sample noise at 15-minute steps draws a ragged, pixelated-looking line; real data drifts instead.
    /// </summary>
    public static double Drift(int i, int seed, int span = 8)
    {
        var k = Math.DivRem(i, span, out var r);
        var f = (1 - Math.Cos(Math.PI * r / span)) / 2;
        return Noise(k, seed) * (1 - f) + Noise(k + 1, seed) * f;
    }

    /// <summary>Repeatable noise in [-0.5, 0.5).</summary>
    public static double Noise(int i, int seed)
    {
        var x = (uint)(i * 374761393 + seed * 668265263);
        x = (x ^ (x >> 13)) * 1274126177;
        return (x ^ (x >> 16)) / (double)uint.MaxValue - 0.5;
    }
}
