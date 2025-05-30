using CustomLogger;
using NetHasher;
using System.Collections.Concurrent;
using System.Text;
using WebAPIService.SSFW;
using XI5;

namespace SSFWServer
{
    public class SSFWUserSessionManager
    {
        private static ConcurrentDictionary<string, (int, UserSession, DateTime)> userSessions = new();

        public static void RegisterUser(string userName, string sessionid, string id, int realuserNameSize)
        {
            if (userSessions.TryGetValue(sessionid, out (int, UserSession, DateTime) sessionEntry))
                UpdateKeepAliveTime(sessionid, sessionEntry);
            else if (userSessions.TryAdd(sessionid, (realuserNameSize, new UserSession { Username = userName, Id = id }, DateTime.Now.AddMinutes(SSFWServerConfiguration.SSFWTTL))))
                LoggerAccessor.LogInfo($"[UserSessionManager] - User '{userName}' successfully registered with SessionId '{sessionid}'.");
            else
                LoggerAccessor.LogError($"[UserSessionManager] - Failed to register User '{userName}' with SessionId '{sessionid}'.");
        }

        public static string? GetUsernameBySessionId(string? sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return null;

            if (userSessions.TryGetValue(sessionId, out (int, UserSession, DateTime) sessionEntry))
                return sessionEntry.Item2.Username;

            return null;
        }

        public static string? GetFormatedUsernameBySessionId(string? sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return null;

            if (userSessions.TryGetValue(sessionId, out (int, UserSession, DateTime) sessionEntry))
            {
                string? userName = sessionEntry.Item2.Username;

                if (!string.IsNullOrEmpty(userName) && userName.Length > sessionEntry.Item1)
                    userName = userName.Substring(0, sessionEntry.Item1);

                return userName;
            }

            return null;
        }

        public static string? GetIdBySessionId(string? sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return null;

            (bool, string?) sessionTuple = IsSessionValid(sessionId, false);

            if (sessionTuple.Item1)
                return sessionTuple.Item2;

            return null;
        }

        public static bool UpdateKeepAliveTime(string sessionid, (int, UserSession, DateTime) sessionEntry = default)
        {
            if (sessionEntry == default)
            {
                if (!userSessions.TryGetValue(sessionid, out sessionEntry))
                    return false;
            }

            DateTime KeepAliveTime = DateTime.Now.AddMinutes(SSFWServerConfiguration.SSFWTTL);

            sessionEntry.Item3 = KeepAliveTime;

            if (userSessions.ContainsKey(sessionid))
            {
                LoggerAccessor.LogInfo($"[SSFWUserSessionManager] - Updating: {sessionEntry.Item2?.Username} session with id: {sessionEntry.Item2?.Id} keep-alive time to:{KeepAliveTime}.");
                userSessions[sessionid] = sessionEntry;
                return true;
            }

            LoggerAccessor.LogError($"[SSFWUserSessionManager] - Failed to update: {sessionEntry.Item2?.Username} session with id: {sessionEntry.Item2?.Id} keep-alive time.");
            return false;
        }

        public static (bool, string?) IsSessionValid(string? sessionId, bool cleanupDeadSessions)
        {
            if (string.IsNullOrEmpty(sessionId))
                return (false, null);

            if (userSessions.TryGetValue(sessionId, out (int, UserSession, DateTime) sessionEntry))
            {
                if (sessionEntry.Item3 > DateTime.Now)
                    return (true, sessionEntry.Item2.Id);
                else if (cleanupDeadSessions)
                {
                    // Clean up expired entry.
                    if (userSessions.TryRemove(sessionId, out sessionEntry))
                        LoggerAccessor.LogWarn($"[SSFWUserSessionManager] - Cleaned: {sessionEntry.Item2.Username} session with id: {sessionEntry.Item2.Id}...");
                    else
                        LoggerAccessor.LogError($"[SSFWUserSessionManager] - Failed to clean: {sessionEntry.Item2.Username} session with id: {sessionEntry.Item2.Id}...");
                }
            }

            return (false, null);
        }

        public static void SessionCleanupLoop(object? state)
        {
            lock (userSessions)
            {
                foreach (var sessionId in userSessions.Keys)
                {
                    IsSessionValid(sessionId, true);
                }
            }
        }
    }

    public class UserSession
    {
        public string? Username { get; set; }
        public string? Id { get; set; }
    }

    public class SSFWLogin : IDisposable
    {
        private string? XHomeClientVersion;
        private string? generalsecret;
        private string? homeClientVersion;
        private string? xsignature;
        private string? key;
        private bool disposedValue;

        public SSFWLogin(string XHomeClientVersion, string generalsecret, string homeClientVersion, string? xsignature, string? key)
        {
            this.XHomeClientVersion = XHomeClientVersion;
            this.generalsecret = generalsecret;
            this.homeClientVersion = homeClientVersion;
            this.xsignature = xsignature;
            this.key = key;
        }

        public string? HandleLogin(byte[]? ticketBuffer, string env)
        {
            if (ticketBuffer != null)
            {
                bool IsRPCN = false;
                string salt = string.Empty;
                string? RPCNsessionIdFallback = null;

                // Extract the desired portion of the binary data
                byte[] extractedData = new byte[0x63 - 0x54 + 1];

                // Copy it
                Array.Copy(ticketBuffer, 0x54, extractedData, 0, extractedData.Length);

                // Convert 0x00 bytes to 0x48 so FileSystem can support it
                for (int i = 0; i < extractedData.Length; i++)
                {
                    if (extractedData[i] == 0x00)
                        extractedData[i] = 0x48;
                }

                XI5Ticket ticket = XI5Ticket.ReadFromBytes(ticketBuffer);

                // invalid ticket
                if (!ticket.Valid)
                {
                    LoggerAccessor.LogWarn($"[SSFWLogin] - {ticket.Username} tried to alter their ticket data");
                    return null;
                }

                // RPCN
                if (ticket.SignatureIdentifier == "RPCN")
                {
                    IsRPCN = true;
                    LoggerAccessor.LogInfo($"[SSFWLogin] - {ticket.Username} logged in and is on RPCN");
                }

                // PSN
                else
                    LoggerAccessor.LogInfo($"[SSFWLogin] - {ticket.Username} logged in and is on PSN");

                (string, string) UserNames = new();
                (string, string) ResultStrings = new();
                (string, string) SessionIDs = new();

                // Convert the modified data to a string
                UserNames.Item2 = ResultStrings.Item2 = Encoding.ASCII.GetString(extractedData) + homeClientVersion;

                // Calculate the MD5 hash of the result
                if (!string.IsNullOrEmpty(xsignature))
                    salt = generalsecret + xsignature + XHomeClientVersion;
                else
                    salt = generalsecret + XHomeClientVersion;

                string hash = DotNetHasher.ComputeMD5String(Encoding.ASCII.GetBytes(ResultStrings.Item2 + salt));

                // Trim the hash to a specific length
                hash = hash[..14];

                // Append the trimmed hash to the result
                ResultStrings.Item2 += hash;

                string sessionIdFallback = GuidGenerator.SSFWGenerateGuid(hash, ResultStrings.Item2);

                SessionIDs.Item2 = GuidGenerator.SSFWGenerateGuid(hash, ResultStrings.Item2, SSFWServerConfiguration.SSFWSessionIdKey);

                if (IsRPCN)
                {
                    // Convert the modified data to a string
                    UserNames.Item1 = ResultStrings.Item1 = Encoding.ASCII.GetString(extractedData) + "RPCN" + homeClientVersion;

                    // Calculate the MD5 hash of the result
                    if (!string.IsNullOrEmpty(xsignature))
                        salt = generalsecret + xsignature + XHomeClientVersion;
                    else
                        salt = generalsecret + XHomeClientVersion;

                    hash = DotNetHasher.ComputeMD5String(Encoding.ASCII.GetBytes(ResultStrings.Item1 + salt));

                    // Trim the hash to a specific length
                    hash = hash[..10];

                    // Append the trimmed hash to the result
                    ResultStrings.Item1 += hash;

                    RPCNsessionIdFallback = GuidGenerator.SSFWGenerateGuid(hash, ResultStrings.Item1);

                    SessionIDs.Item1 = GuidGenerator.SSFWGenerateGuid(hash, ResultStrings.Item1, SSFWServerConfiguration.SSFWSessionIdKey);
                }

                if (!string.IsNullOrEmpty(UserNames.Item1) && !SSFWServerConfiguration.SSFWCrossSave) // RPCN confirmed.
                {
                    SSFWUserSessionManager.RegisterUser(UserNames.Item1, SessionIDs.Item1!, ResultStrings.Item1!, ticket.Username.Length);

                    if (SSFWAccountManagement.AccountExists(UserNames.Item2, SessionIDs.Item2))
                        SSFWAccountManagement.CopyAccountProfile(UserNames.Item2, UserNames.Item1, SessionIDs.Item2, SessionIDs.Item1!, key);
                    else if (SSFWAccountManagement.AccountExists(UserNames.Item2, sessionIdFallback))
                        SSFWAccountManagement.CopyAccountProfile(UserNames.Item2, UserNames.Item1, sessionIdFallback, SessionIDs.Item1!, key);
                }
                else
                {
                    IsRPCN = false;

                    SSFWUserSessionManager.RegisterUser(UserNames.Item2, SessionIDs.Item2, ResultStrings.Item2, ticket.Username.Length);
                }

                int logoncount = SSFWAccountManagement.ReadOrMigrateAccount(extractedData, IsRPCN ? UserNames.Item1 : UserNames.Item2, IsRPCN ? SessionIDs.Item1 : SessionIDs.Item2, key);

                if (logoncount <= 0)
                {
                    logoncount = SSFWAccountManagement.ReadOrMigrateAccount(extractedData, IsRPCN ? UserNames.Item1 : UserNames.Item2, IsRPCN ? RPCNsessionIdFallback : sessionIdFallback, key);

                    if (logoncount <= 0)
                    {
                        LoggerAccessor.LogError($"[SSFWLogin] - Invalid Account or LogonCount value for user: {(IsRPCN ? UserNames.Item1 : UserNames.Item2)}");
                        return null;
                    }
                }

                if (IsRPCN && Directory.Exists($"{SSFWServerConfiguration.SSFWStaticFolder}/AvatarLayoutService/{env}/{ResultStrings.Item2}") && !Directory.Exists($"{SSFWServerConfiguration.SSFWStaticFolder}/AvatarLayoutService/{env}/{ResultStrings.Item1}"))
                    SSFWDataMigrator.MigrateSSFWData(SSFWServerConfiguration.SSFWStaticFolder, ResultStrings.Item2, ResultStrings.Item1);

                string? resultString = IsRPCN ? ResultStrings.Item1 : ResultStrings.Item2;

                if (string.IsNullOrEmpty(resultString))
                {
                    LoggerAccessor.LogError($"[SSFWLogin] - Invalid ResultString value for user: {(IsRPCN ? UserNames.Item1 : UserNames.Item2)}");
                    return null;
                }

                Directory.CreateDirectory($"{SSFWServerConfiguration.SSFWStaticFolder}/LayoutService/{env}/person/{resultString}");
                Directory.CreateDirectory($"{SSFWServerConfiguration.SSFWStaticFolder}/RewardsService/{env}/rewards/{resultString}");
                Directory.CreateDirectory($"{SSFWServerConfiguration.SSFWStaticFolder}/RewardsService/trunks-{env}/trunks");
                Directory.CreateDirectory($"{SSFWServerConfiguration.SSFWStaticFolder}/AvatarLayoutService/{env}/{resultString}");

                if (File.Exists(SSFWServerConfiguration.ScenelistFile))
                {
                    bool handled = false;

                    IDictionary<string, string> scenemap = ScenelistParser.sceneDictionary;

                    if (File.Exists($"{SSFWServerConfiguration.SSFWStaticFolder}/LayoutService/{env}/person/{resultString}/mylayout.json")) // Migrate data.
                    {
                        // Parsing each value in the dictionary
                        foreach (var kvp in new Services.SSFWLayoutService(key).SSFWGetLegacyFurnitureLayouts($"{SSFWServerConfiguration.SSFWStaticFolder}/LayoutService/{env}/person/{resultString}/mylayout.json"))
                        {
                            if (kvp.Key == "00000000-00000000-00000000-00000004")
                            {
                                File.WriteAllText($"{SSFWServerConfiguration.SSFWStaticFolder}/LayoutService/{env}/person/{resultString}/HarborStudio.json", kvp.Value);
                                handled = true;
                            }
                            else
                            {
                                string scenename = scenemap.FirstOrDefault(x => x.Value == SSFWMisc.ExtractPortion(kvp.Key, 13, 18)).Key;
                                if (!string.IsNullOrEmpty(scenename))
                                {
                                    if (File.Exists($"{SSFWServerConfiguration.SSFWStaticFolder}/LayoutService/{env}/person/{resultString}/{kvp.Key}.json")) // SceneID now mapped, so SceneID based file has become obsolete.
                                        File.Delete($"{SSFWServerConfiguration.SSFWStaticFolder}/LayoutService/{env}/person/{resultString}/{kvp.Key}.json");

                                    File.WriteAllText($"{SSFWServerConfiguration.SSFWStaticFolder}/LayoutService/{env}/person/{resultString}/{scenename}.json", kvp.Value);
                                    handled = true;
                                }
                            }

                            if (!handled)
                                File.WriteAllText($"{SSFWServerConfiguration.SSFWStaticFolder}/LayoutService/{env}/person/{resultString}/{kvp.Key}.json", kvp.Value);

                            handled = false;
                        }

                        File.Delete($"{SSFWServerConfiguration.SSFWStaticFolder}/LayoutService/{env}/person/{resultString}/mylayout.json");
                    }
                    else if (!File.Exists($"{SSFWServerConfiguration.SSFWStaticFolder}/LayoutService/{env}/person/{resultString}/HarborStudio.json"))
                        File.WriteAllText($"{SSFWServerConfiguration.SSFWStaticFolder}/LayoutService/{env}/person/{resultString}/HarborStudio.json", SSFWMisc.HarbourStudioLayout);
                }
                else
                {
                    if (!File.Exists($"{SSFWServerConfiguration.SSFWStaticFolder}/LayoutService/{env}/person/{resultString}/mylayout.json"))
                        File.WriteAllText($"{SSFWServerConfiguration.SSFWStaticFolder}/LayoutService/{env}/person/{resultString}/mylayout.json", SSFWMisc.LegacyLayoutTemplate);
                }

                if (!File.Exists($"{SSFWServerConfiguration.SSFWStaticFolder}/RewardsService/{env}/rewards/{resultString}/mini.json"))
                    File.WriteAllText($"{SSFWServerConfiguration.SSFWStaticFolder}/RewardsService/{env}/rewards/{resultString}/mini.json", SSFWServerConfiguration.SSFWMinibase);
                if (!File.Exists($"{SSFWServerConfiguration.SSFWStaticFolder}/RewardsService/trunks-{env}/trunks/{resultString}.json"))
                    File.WriteAllText($"{SSFWServerConfiguration.SSFWStaticFolder}/RewardsService/trunks-{env}/trunks/{resultString}.json", "{\"objects\":[]}");
                if (!File.Exists($"{SSFWServerConfiguration.SSFWStaticFolder}/AvatarLayoutService/{env}/{resultString}/list.json"))
                    File.WriteAllText($"{SSFWServerConfiguration.SSFWStaticFolder}/AvatarLayoutService/{env}/{resultString}/list.json", "[]");

                return $"{{\"session\":[{{\"@id\":\"{(IsRPCN ? SessionIDs.Item1 : SessionIDs.Item2)}\",\"person\":{{\"@id\":\"{resultString}\",\"logonCount\":\"{logoncount}\"}}}}]}}";
            }

            return null;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    XHomeClientVersion = null;
                    generalsecret = null;
                    homeClientVersion = null;
                    xsignature = null;
                    key = null;
                }

                // TODO: libérer les ressources non managées (objets non managés) et substituer le finaliseur
                // TODO: affecter aux grands champs une valeur null
                disposedValue = true;
            }
        }

        // // TODO: substituer le finaliseur uniquement si 'Dispose(bool disposing)' a du code pour libérer les ressources non managées
        // ~SSFWLogin()
        // {
        //     // Ne changez pas ce code. Placez le code de nettoyage dans la méthode 'Dispose(bool disposing)'
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Ne changez pas ce code. Placez le code de nettoyage dans la méthode 'Dispose(bool disposing)'
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}