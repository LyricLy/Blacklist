using SML;
using System;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kadlet;
using Game.Chat;
using Shared.Chat;
using Server.Shared.State;
using Server.Shared.State.Chat;
using Server.Shared.Messages;
using Home.Services;
using Services;
using HarmonyLib;

namespace BlacklistMod;

using BL = Dictionary<string, HashSet<string>>;

class ListParseException : Exception
{
    static KdlPrintOptions Compact = new KdlPrintOptions {
        IndentSize = 0,
        Newline = " ",
        TerminateNodesWithSemicolon = true,
        ExponentChar = 'e',
    };

    public ListParseException(string message) : base(message)
    {}
    public ListParseException(string message, KdlNode badNode) : base($"{message}: {badNode.ToKdlString(Compact).TrimEnd(';')}")
    {}
}

[Mod.SalemMod]
class Blacklist
{
    static BL theList;
    static readonly HttpClient client = new HttpClient();
    static readonly KdlReader reader = new KdlReader();

    static void AddToList(BL names, string name, Action<HashSet<string>> op)
    {
        if (!names.TryGetValue(name, out var listList))
        {
            listList = new HashSet<string>();
            names[name] = listList;
        }
        op(listList);
    }

    async static Task<KdlDocument> FetchList(string url)
    {
        try
        {
            string resp = await client.GetStringAsync(url);
            return reader.Parse(resp);
        }
        catch (UriFormatException e)
        {
            throw new ListParseException($"'{url}' is a malformed URL: {e.Message}");
        }
        catch (ArgumentException e)
        {
            throw new ListParseException($"Refusing to fetch {url}: {e.Message}");
        }
        catch (InvalidOperationException)
        {
            throw new ListParseException($"Refusing to fetch {url}: URL must be absolute");
        }
        catch (HttpRequestException e)
        {
            throw new ListParseException($"Failed to fetch {url}: {e.InnerException.Message}");
        }
        catch (KdlException e)
        {
            throw new ListParseException($"{url} returned an invalid document:\n{e.Message}");
        }
    }

    async static Task<BL> ParseList(KdlDocument doc, string listName)
    {
        var names = new BL();
        var inno = new HashSet<string>();

        foreach (var node in doc.Nodes)
        {
            switch (node.Identifier)
            {
                case "-":
                    if (node.Children != null)
                    {
                        throw new ListParseException("Node should not have children", node);
                    }
                    if (node.Arguments is [KdlString name])
                    {
                        AddToList(names, name.Value.ToLower(), listList => listList.Add(listName));
                    }
                    else
                    {
                        throw new ListParseException("Improper arguments", node);
                    }
                    break;
                case "!":
                    if (node.Children != null)
                    {
                        throw new ListParseException("Node should not have children", node);
                    }
                    if (node.Arguments is [KdlString innoName])
                    {
                        inno.Add(innoName.Value.ToLower());
                    }
                    else
                    {
                        throw new ListParseException("Improper arguments", node);
                    }
                    break;
                case "list":
                    string chosenName = node.Arguments switch
                    {
                        [] => "",
                        [KdlString c] => c.Value,
                        _ => throw new ListParseException("Improper arguments", node),
                    };
                    string newName = !chosenName.Any() ? listName : !listName.Any() ? chosenName : $"{listName}/{chosenName}";

                    KdlDocument childDoc = (node.Children != null, node.Properties.TryGetValue("src", out var srcUrl)) switch
                    {
                        (true, true) => throw new ListParseException("Node should have children or 'src' property, not both", node),
                        (false, false) => throw new ListParseException("Node should have children or a 'src' property", node),
                        (true, false) => node.Children,
                        (false, true) when srcUrl is KdlString url => await FetchList(url),
                        _ => throw new ListParseException("'src' must be a string", node),
                    };

                    foreach (var (key, val) in await ParseList(childDoc, newName))
                    {
                        AddToList(names, key, listList => listList.UnionWith(val));
                    }
                    break;
                default:
                    throw new ListParseException("Unknown node name", node);
            }
        }

        // never blacklist ourselves
        names.Remove(Service.Home.UserService.UserInfo.AccountName);

        foreach (var name in inno)
        {
            names.Remove(name);
        }
        return names;
    }

    public static void Start()
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        Harmony.CreateAndPatchAll(typeof(Blacklist));
    }

	[HarmonyPatch(typeof(HomeLocalizationService), "RebuildStringTables")]
	[HarmonyPostfix]
	static void AddLocalizationKeys(HomeLocalizationService __instance)
	{
		__instance.stringTable_.Add("GUI_BLACKLIST_NOTICE", "[[@{0}]] is blacklisted! {1}");
		__instance.stringTable_.Add("GUI_BLACKLIST_KICK_NOTICE", "Kicking blacklisted player <b>{0}</b>. {1}");
		__instance.stringTable_.Add("GUI_BLACKLIST_PARSE_ERROR", "Failed to load blacklist.\n{0}");
		__instance.stringTable_.Add("GUI_BLACKLIST_LOADING", "Loading blacklist...");
		__instance.stringTable_.Add("GUI_BLACKLIST_LOADED", "Blacklist loaded with {0} entries!");
		__instance.stringTable_.Add("GUI_BLACKLIST_BAD_ERROR", "Something went very wrong!\n{0}");
	}

    static void PostErrorRaw(PooledChatController controller, string msg)
    {
        var message = new ChatLogMessage();
        message.chatLogEntry = new ChatLogCustomTextEntry
        {
            customText = msg,
            style = "error",
            showInChat = true,
            showInChatLog = false,
        };
        Service.Game.Sim.simulation.incomingChatMessage.ForceSet(message);
    }

    static void PostError(PooledChatController controller, string key) {
        PostErrorRaw(controller, controller.l10n(key));
    }

    static void PostError(PooledChatController controller, string key, object arg) {
        PostErrorRaw(controller, controller.l10n(key, arg));
    }

    static void PostError(PooledChatController controller, string key, object arg, object arg2) {
        PostErrorRaw(controller, controller.l10n(key, arg, arg2));
    }

    static void CheckBlacklist(PooledChatController controller, DiscussionPlayerState discussionPlayerState)
    {
        if (theList == null || !theList.TryGetValue(discussionPlayerState.accountName.ToLower(), out var listNames))
        {
            return;
        }

        var listDigest = String.Join(", ", listNames);
        if (listDigest.Any()) listDigest = $"({listDigest})";

        string condition = ModSettings.GetString("Send warnings", "lyricly.blacklist");
        bool host = Pepper.IsCustomMode() && Pepper.AmIHost();

        if (ModSettings.GetBool("Kick automatically", "lyricly.blacklist") && host)
        {
            PostError(controller, "GUI_BLACKLIST_KICK_NOTICE", discussionPlayerState.accountName, listDigest);
            Service.Game.Sim.simulation.SendHostAction(discussionPlayerState.position, HostActionType.Kick);
        }
        else if (condition == "Always" || condition == "Only when in custom" && Pepper.IsCustomMode() || host)
        {
            PostError(controller, "GUI_BLACKLIST_NOTICE", discussionPlayerState.position + 1, listDigest);
        }
    }

    async static Task LoadList(PooledChatController controller)
    {
        theList = null;

        var path = Path.GetDirectoryName(UnityEngine.Application.dataPath) + "/SalemModLoader/ModFolders/Blacklist/config.kdl";
        KdlDocument document;

        try
        {
            using var fs = File.OpenRead(path);
            document = reader.Parse(fs);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var defaultConfig = typeof(Blacklist).Assembly.GetManifestResourceStream("Blacklist.resources.config.kdl");
            document = reader.Parse(defaultConfig);
            defaultConfig.Seek(0, SeekOrigin.Begin);
            using var fs = File.OpenWrite(path);
            defaultConfig.CopyTo(fs);
        }
        catch (KdlException e)
        {
            PostError(controller, "GUI_BLACKLIST_PARSE_ERROR", e.Message);
            return;
        }

        bool verbose = ModSettings.GetBool("Verbose mode", "lyricly.blacklist");
        if (verbose)
        {
            PostError(controller, "GUI_BLACKLIST_LOADING");
        }

        try
        {
            theList = await ParseList(document, "");
            if (verbose)
            {
                PostError(controller, "GUI_BLACKLIST_LOADED", theList.Count);
            }
        }
        catch (ListParseException e)
        {
            PostError(controller, "GUI_BLACKLIST_PARSE_ERROR", e.Message);
        }
        catch (Exception e)
        {
            PostError(controller, "GUI_BLACKLIST_BAD_ERROR", e.ToString());
        }

        foreach (var obs in Service.Game.Sim.info.discussionPlayers)
        {
            CheckBlacklist(controller, obs.Data);
        }
    }

    [HarmonyPatch(typeof(PooledChatController), "ProcessAlreadyJoined")]
    [HarmonyPostfix]
    static void OnJoiningLobby(PooledChatController __instance)
    {
        if (Pepper.IsLobbyPhase() && __instance.chatWindowType == ChatWindowType.Chat)
        {
            LoadList(__instance);
        }
    }

    [HarmonyPatch(typeof(PooledChatController), "HandleOnDiscussionPlayerUpdated")]
    [HarmonyPostfix]
    static void OnPlayerJoinOrDisconnect(PooledChatController __instance, DiscussionPlayerState discussionPlayerState)
    {
        if (Pepper.IsLobbyPhase() && __instance.chatWindowType == ChatWindowType.Chat && discussionPlayerState.valid && discussionPlayerState.connected)
        {
            CheckBlacklist(__instance, discussionPlayerState);
        }
    }
}
