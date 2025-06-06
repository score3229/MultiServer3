using MultiSocks.Aries.DataStore;

namespace MultiSocks.Aries.Messages
{
    public class Auth : AbstractMessage
    {
        public override string _Name { get => "auth"; }

        public override void Process(AbstractAriesServer context, AriesClient client)
        {
            if (context is not MatchmakerServer mc) return;

            string VERS = GetInputCacheValue("VERS") ?? string.Empty;
            string SKU = GetInputCacheValue("SKU") ?? string.Empty;
            string? MADDR = GetInputCacheValue("MADDR");
            string? NAME = GetInputCacheValue("NAME");
            string? PASS = GetInputCacheValue("PASS");
            string? MAC = GetInputCacheValue("MAC");
            string? LOC = GetInputCacheValue("LOC");
            string? TOKEN = GetInputCacheValue("TOKEN");

            client.VERS = VERS;
            client.SKU = SKU;

            switch (VERS)
            {
                case "BURNOUT5/ISLAND":
                case "BURNOUT5/TROPHIES":
                case "BURNOUT5/31":
                case "BURNOUT5/PSN_DAVIS":
                case "BOTTEST":
                    if (SKU == "PS3")
                    {
                        string[]? maddrparams = MADDR?.Split('$');

                        if (maddrparams != null)
                            NAME = maddrparams.FirstOrDefault();
                    }
                    break;

            }

            if (!string.IsNullOrEmpty(NAME))
            {
                DbAccount? user = AriesServer.Database?.GetByName(NAME);
                if (user != null)
                {
                    mc.TryLogin(user, client, PASS, LOC ?? "enUS", MAC, TOKEN);
                    return;
                }
            }
            else if (!string.IsNullOrEmpty(TOKEN))
            {
                mc.TryGuestLogin(client, PASS, LOC ?? "enUS", MAC, TOKEN);
                return;
            }

            client.SendMessage(new AuthImst());
        }
    }
}
