using MultiSocks.Aries.Model;

namespace MultiSocks.Aries.Messages
{
    public class Gcre : AbstractMessage
    {
        public override string _Name { get => "gcre"; }

        public override void Process(AbstractAriesServer context, AriesClient client)
        {
            if (context is not MatchmakerServer mc) return;

            AriesUser? user = client.User;
            if (user == null) return;

            string? CUSTFLAGS = GetInputCacheValue("CUSTFLAGS");
            string? USERPARAMS = GetInputCacheValue("USERPARAMS");
            string? FORCE_LEAVE = GetInputCacheValue("FORCE_LEAVE");
            string? SYSFLAGS = GetInputCacheValue("SYSFLAGS");
            string? SEED = GetInputCacheValue("SEED");
            string? PASS = GetInputCacheValue("PASS");
            string? PARAMS = GetInputCacheValue("PARAMS");
            string? NAME = GetInputCacheValue("NAME");

            if (!string.IsNullOrEmpty(USERPARAMS))
                user.SetParametersFromString(USERPARAMS);

            if (!string.IsNullOrEmpty(FORCE_LEAVE) && FORCE_LEAVE == "1" && user.CurrentGame != null)
            {
                AriesGame prevGame = user.CurrentGame;

                if (prevGame.RemovePlayerByUsername(user.Username))
                    mc.Games.RemoveGame(prevGame);
            }

            int? parsedMinSize = int.TryParse(GetInputCacheValue("MINSIZE"), out int minSize) ? minSize : null;
            int? parsedMaxSize = int.TryParse(GetInputCacheValue("MAXSIZE"), out int maxSize) ? maxSize : null;
            int? parsedPriv = int.TryParse(GetInputCacheValue("PRIV"), out int priv) ? priv : null;

            if (user.Connection?.VERS == "FLM/A1")
            {
                SEED = "0";
                CUSTFLAGS = "0";
                parsedPriv = 0;
            }

            // Check if any of the nullable variables are null before calling CreateGame
            if (parsedMinSize.HasValue && parsedMaxSize.HasValue && !string.IsNullOrEmpty(CUSTFLAGS) &&
                !string.IsNullOrEmpty(PARAMS) && !string.IsNullOrEmpty(NAME) && parsedPriv.HasValue &&
                !string.IsNullOrEmpty(SEED) && !string.IsNullOrEmpty(SYSFLAGS) && !string.IsNullOrEmpty(user.Username))
            {
                AriesGame? game = mc.Games.AddGame(parsedMaxSize.Value, parsedMinSize.Value, CUSTFLAGS, PARAMS, NAME, parsedPriv.Value != 0, SEED, SYSFLAGS, PASS, 0);

                if (game != null)
                {
                    if (game.MinSize > 1) // Could it be more than 2?
                    {
                        if (game.Users.GetUserByName("brobot24") == null)
                            game.AddHost(mc.Users.GetUserByName("brobot24"));

                        if (game.Users.GetUserByName(user.Username) == null)
                            game.AddGPSHost(user);
                    }
                    else
                    {
                        if (game.Users.GetUserByName(user.Username) == null)
                            game.AddGPSHost(user);
                    }

                    user.CurrentGame = game;

                    client.SendMessage(game.GetGameDetails(_Name));

                    user.SendPlusWho(user, context.Project);

                    game.BroadcastPopulation(mc);
                }
                else
                    client.SendMessage(new GcreDupl());
            }
            else
                client.SendMessage(new GcreInvp());
        }
    }
}
