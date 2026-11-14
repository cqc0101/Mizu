namespace Mizu.Core.Services;

public static class OfflinePoetry
{
    // Public-domain classical texts; independent of the online API and its cache.
    private static PoetrySnapshot P(string text, string author, string title, string dynasty) => new(text, author, title, default) { Dynasty = dynasty, Source = "内置公版诗库" };
    public static IReadOnlyList<PoetrySnapshot> Entries { get; } = Array.AsReadOnly(new[] {
        P("海内存知己，天涯若比邻。", "王勃", "送杜少府之任蜀州", "唐代"),
        P("长风破浪会有时，直挂云帆济沧海。", "李白", "行路难·其一", "唐代"),
        P("天生我材必有用，千金散尽还复来。", "李白", "将进酒", "唐代"),
        P("桃花潭水深千尺，不及汪伦送我情。", "李白", "赠汪伦", "唐代"),
        P("举头望明月，低头思故乡。", "李白", "静夜思", "唐代"),
        P("明月松间照，清泉石上流。", "王维", "山居秋暝", "唐代"),
        P("独在异乡为异客，每逢佳节倍思亲。", "王维", "九月九日忆山东兄弟", "唐代"),
        P("行到水穷处，坐看云起时。", "王维", "终南别业", "唐代"),
        P("欲穷千里目，更上一层楼。", "王之涣", "登鹳雀楼", "唐代"),
        P("会当凌绝顶，一览众山小。", "杜甫", "望岳", "唐代"),
        P("好雨知时节，当春乃发生。", "杜甫", "春夜喜雨", "唐代"),
        P("露从今夜白，月是故乡明。", "杜甫", "月夜忆舍弟", "唐代"),
        P("几处早莺争暖树，谁家新燕啄春泥。", "白居易", "钱塘湖春行", "唐代"),
        P("竹外桃花三两枝，春江水暖鸭先知。", "苏轼", "惠崇春江晚景·其一", "宋代"),
        P("但愿人长久，千里共婵娟。", "苏轼", "水调歌头·明月几时有", "宋代"),
        P("稻花香里说丰年，听取蛙声一片。", "辛弃疾", "西江月·夜行黄沙道中", "宋代"),
        P("山重水复疑无路，柳暗花明又一村。", "陆游", "游山西村", "宋代"),
        P("生当作人杰，死亦为鬼雄。", "李清照", "夏日绝句", "宋代"),
        P("采菊东篱下，悠然见南山。", "陶渊明", "饮酒·其五", "晋代"),
        P("春眠不觉晓，处处闻啼鸟。", "孟浩然", "春晓", "唐代"),
        P("绿树村边合，青山郭外斜。", "孟浩然", "过故人庄", "唐代"),
        P("停车坐爱枫林晚，霜叶红于二月花。", "杜牧", "山行", "唐代"),
        P("晴空一鹤排云上，便引诗情到碧霄。", "刘禹锡", "秋词·其一", "唐代"),
        P("天街小雨润如酥，草色遥看近却无。", "韩愈", "早春呈水部张十八员外·其一", "唐代"),
        P("何当共剪西窗烛，却话巴山夜雨时。", "李商隐", "夜雨寄北", "唐代"),
        P("孤舟蓑笠翁，独钓寒江雪。", "柳宗元", "江雪", "唐代"),
        P("春风又绿江南岸，明月何时照我还？", "王安石", "泊船瓜洲", "宋代"),
        P("接天莲叶无穷碧，映日荷花别样红。", "杨万里", "晓出净慈寺送林子方", "宋代"),
        P("等闲识得东风面，万紫千红总是春。", "朱熹", "春日", "宋代"),
        P("不知细叶谁裁出，二月春风似剪刀。", "贺知章", "咏柳", "唐代")
    });
    public static PoetrySnapshot Pick(DateTimeOffset now, string? previous = null, bool manual = false)
    {
        var index = DateOnly.FromDateTime(now.LocalDateTime).DayNumber % Entries.Count;
        if (manual) { var old = Entries.ToList().FindIndex(p => p.Content == previous); index = Random.Shared.Next(Entries.Count - (old >= 0 ? 1 : 0)); if (old >= 0 && index >= old) index++; }
        return Entries[index] with { FetchedAt = now };
    }
}
