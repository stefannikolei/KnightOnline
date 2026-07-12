namespace OpenKO.Servers.Ebenezer;

/// <summary>
/// One market board side (the EbenezerApp m_sBuyID/m_sSellID array family):
/// 500 post slots with poster, title, message, price and post time.
/// </summary>
public sealed class MarketBbsBoard
{
    public const int MaxPosts = 500; // MAX_BBS_POST

    public readonly short[] PosterId = CreateIds();
    public readonly string[] Title = CreateStrings();
    public readonly string[] Message = CreateStrings();
    public readonly int[] Price = new int[MaxPosts];
    public readonly double[] StartTime = new double[MaxPosts];

    private static short[] CreateIds()
    {
        var ids = new short[MaxPosts];
        Array.Fill(ids, (short)-1);
        return ids;
    }

    private static string[] CreateStrings()
    {
        var strings = new string[MaxPosts];
        Array.Fill(strings, string.Empty);
        return strings;
    }

    /// <summary>EbenezerApp::MarketBBSBuyDelete / MarketBBSSellDelete.</summary>
    public void Delete(int index)
    {
        PosterId[index] = -1;
        Title[index] = string.Empty;
        Message[index] = string.Empty;
        Price[index] = 0;
        StartTime[index] = 0.0;
    }

    /// <summary>CUser::MarketBBS*PostFilter — shift posts over the empty slots.</summary>
    public void Compact()
    {
        int emptyCounter = 0;
        for (int i = 0; i < MaxPosts; i++)
        {
            if (PosterId[i] == -1)
            {
                emptyCounter++;
                continue;
            }

            if (emptyCounter > 0)
            {
                PosterId[i - emptyCounter] = PosterId[i];
                Title[i - emptyCounter] = Title[i];
                Message[i - emptyCounter] = Message[i];
                Price[i - emptyCounter] = Price[i];
                StartTime[i - emptyCounter] = StartTime[i];
                Delete(i);
            }
        }
    }
}
