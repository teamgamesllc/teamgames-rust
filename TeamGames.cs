using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries;
using Newtonsoft.Json;
using System.Collections.Generic;
using System;
using UnityEngine;
using Epic.OnlineServices.Ecom;

namespace Oxide.Plugins
{
    [Info("TeamGames Store", "TeamGames", "1.0.9")]
    [Description("Official support for the TeamGames monetization platform.")]
    public class TeamGames : RustPlugin
    {
        private const string ApiUrl = "https://api.teamgames.io/api/v3/store/transaction/update";
        private string apiKey;
        private string claimCommand;
        private string secretCommand;
        private Dictionary<string, string> headers;

        protected override void LoadConfig()
        {
            base.LoadConfig();
        
            if (Config["store-secret-key"] == null) Config["store-secret-key"] = "default-key";
            if (Config["claim-command"] == null) Config["claim-command"] = "tgclaim";
            if (Config["secret-command"] == null) Config["secret-command"] = "tgsecret";
            SaveConfig();
            apiKey = Config["store-secret-key"] as string ?? "default-key";
            claimCommand = Config["claim-command"] as string ?? "tgclaim";
            secretCommand = Config["secret-command"] as string ?? "tgsecret";
        }

        private void Init()
        {
            apiKey = Config["store-secret-key"] as string ?? "default-key";
            claimCommand = Config["claim-command"] as string ?? "tgclaim";
            secretCommand = Config["secret-command"] as string ?? "tgsecret";

            headers = new Dictionary<string, string>
            {
                ["X-API-Key"] = apiKey,
                ["Content-Type"] = "application/json"
            };
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["ApiOffline"] = "API Services are currently offline. Please check back shortly.",
                ["CommandReserved"] = "Command Reserved for the permission group teamgames.admin",
                ["SecretUsage"] = "Usage: /teamgames.secret <secret>",
                ["SecretUpdated"] = "Store secret key has been updated.",
                ["ErrorProcessing"] = "An error occurred while processing your request. Please try again later.",
                ["NullTransaction"] = "Encountered a null transaction object.",
                ["InvalidAmount"] = "Invalid product amount: {0}",
                ["ItemNotFound"] = "Item {0} not found.",
                ["CreatingItem"] = "Creating {0} of {1}.",
                ["ItemGiven"] = "Gave {0} {1} to {2}.",
                ["ItemDropped"] = "Dropped {0} {1} to {2}.",
                ["FailedToCreate"] = "Failed to create item {0}.",
                ["SetCommandUsage"] = "Usage: /tgsetcmd <claim|secret> <newname>",
                ["InvalidCommandType"] = "Invalid command type. Use 'claim' or 'secret'.",
                ["CommandUpdated"] = "{0} command has been updated to /{1}.",
                ["TeamGamesMessage"] = "{0}"
            }, this);
        }

        [ChatCommand("tg.claim")]
        private void ClaimCommand(BasePlayer player, string command, string[] args)
        {
            var postData = new Dictionary<string, string> { ["playerName"] = player.UserIDString };
            string jsonData = JsonConvert.SerializeObject(postData);

            webrequest.Enqueue(ApiUrl, jsonData, (code, response) => HandleWebResponse(player, code, response), this, RequestMethod.POST, headers);
        }

        [ChatCommand("tg.secret")]
        private void SetSecretCommand(BasePlayer player, string command, string[] args)
        {
            bool isRcon = player?.net?.connection?.authLevel == 2;
            bool hasPermission = permission.UserHasPermission(player.UserIDString, "teamgames.admin");

            if (!isRcon && !hasPermission)
            {
                player.ChatMessage(Lang("CommandReserved", player.UserIDString));
                return;
            }

            if (args.Length != 1)
            {
                player.ChatMessage(Lang("SecretUsage", player.UserIDString));
                return;
            }

            apiKey = args[0];
            Config["store-secret-key"] = apiKey;
            SaveConfig();

            headers["X-API-Key"] = apiKey;

            player.ChatMessage(Lang("SecretUpdated", player.UserIDString));
            PrintWarning($"Store secret key has been updated by {player?.displayName ?? "RCON"}.");
        }


        [ChatCommand("tg.setcmd")]
        private void SetCommandName(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "teamgames.admin"))
            {
                player.ChatMessage(Lang("CommandReserved", player.UserIDString));
                return;
            }
            if (args.Length != 2)
            {
                player.ChatMessage(Lang("SetCommandUsage", player.UserIDString));
                return;
            }

            string cmdType = args[0].ToLower();
            string newName = args[1].ToLower();

            switch (cmdType)
            {
                case "claim":
                    Config["claim-command"] = newName;
                    claimCommand = newName;
                    break;
                case "secret":
                    Config["secret-command"] = newName;
                    secretCommand = newName;
                    break;
                default:
                    player.ChatMessage(Lang("InvalidCommandType", player.UserIDString));
                    return;
            }

            SaveConfig();
            player.ChatMessage(Lang("CommandUpdated", player.UserIDString, cmdType, newName));
            PrintWarning($"{cmdType} command has been updated to /{newName} by {player.displayName}.");
        }

        private void HandleWebResponse(BasePlayer player, int code, string response)
        {
            if (string.IsNullOrEmpty(response) || code != 200)
            {
                PrintWarning($"Failed to fetch transactions for {player.displayName}: {response ?? "No response"} (Code: {code})");
                player.ChatMessage(Lang("ApiOffline", player.UserIDString));
                return;
            }

            try
            {
                var transactions = JsonConvert.DeserializeObject<Transaction[]>(response);
                if (transactions != null)
                {
                    ProcessTransactions(player, transactions);
                }
                else
                {
                    PrintWarning("No transactions found in the response.");
                    player.ChatMessage(Lang("ErrorProcessing", player.UserIDString));
                }
            }
            catch (JsonException ex)
            {
                PrintWarning($"Error parsing JSON response: {ex.Message}");
                player.ChatMessage(Lang("ErrorProcessing", player.UserIDString));
            }
        }

        private void ProcessTransactions(BasePlayer player, Transaction[] transactions)
        {
            if (transactions.Length == 1 && transactions[0].message != null)
            {
                player.ChatMessage(Lang("TeamGamesMessage", player.UserIDString, transactions[0].message));
                return;
            }

            foreach (var transaction in transactions)
            {
                if (transaction == null)
                {
                    player.ChatMessage(Lang("NullTransaction", player.UserIDString));
                    continue;
                }

                if (transaction.product_amount < 1)
                {
                    player.ChatMessage(Lang("InvalidAmount", player.UserIDString, transaction.product_amount));
                    continue;
                }

                string itemName = ParseItemName(transaction.product_id_string);
                ItemDefinition itemDefinition = ItemManager.FindItemDefinition(itemName);
                if (itemDefinition == null)
                {
                    player.ChatMessage(Lang("ItemNotFound", player.UserIDString, itemName));
                    continue;
                }

                player.ChatMessage(Lang("CreatingItem", player.UserIDString, transaction.product_amount, itemName));
                Item item = ItemManager.Create(itemDefinition, transaction.product_amount);
                if (item != null)
                {
                    bool given = player.inventory.GiveItem(item);
                    if (!given)
                    {
                        item.Drop(player.GetDropPosition(), player.GetDropVelocity());
                        player.ChatMessage(Lang("ItemDropped", player.UserIDString, transaction.product_amount, itemName, player.displayName));
                    }
                    else
                    {
                        player.ChatMessage(Lang("ItemGiven", player.UserIDString, transaction.product_amount, itemName, player.displayName));
                    }
                }
                else
                {
                    player.ChatMessage(Lang("FailedToCreate", player.UserIDString, itemName));
                }
            }
        }

        private string ParseItemName(string productIdentifier)
        {
            if (string.IsNullOrEmpty(productIdentifier))
            {
                return null;
            }

            var parts = productIdentifier.Split(':');
            return parts.Length > 1 ? parts[1] : parts[0];
        }

        private string Lang(string key, string id = null, params object[] args)
        {
            string format = lang.GetMessage(key, this, id);
            try
            {
                return string.Format(format, args);
            }
            catch (FormatException ex)
            {
                PrintWarning($"Formatting error for key '{key}': {ex.Message}");
                return format;
            }
        }

        private class Transaction
        {
            public string player_name { get; set; }
            public string product_id_string { get; set; }
            public int product_amount { get; set; }
            public string message { get; set; }
        }
    }
}
