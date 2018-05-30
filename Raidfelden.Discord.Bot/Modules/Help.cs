﻿using Discord;
using Discord.Commands;
using Raidfelden.Discord.Bot.Services;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Raidfelden.Discord.Bot.Modules
{
	public class Help : ModuleBase<SocketCommandContext>
	{
        public Help(IConfigurationService configurationService)
        {
            ConfigurationService = configurationService;
        }

        protected IConfigurationService ConfigurationService { get; private set; }

        [Command("help")]
		public async Task HelpAsync()
		{
            foreach(var module in GetModules())
            {
                await ReplyEmbed(module);
            }
		}

        private IEnumerable<ModuleInfo> GetModules()
        {
            var guild = ConfigurationService.GetGuildConfiguration(Context);
            var prefix = ConfigurationService.GetCommandPrefix(Context);
            var raid = new ModuleInfo
            {
                Module = "raids",
                Commands = { new CommandInfo
                    {
                        Command = "add",
                        Description = "Ermöglicht das hinzufügen von Raids auf der https://pg.festzeit.ch Karte",
                        Examples = new Dictionary<string, string>
                        {
                            { "Raid in Ei-Form eintragen", prefix + "raids add <ArenaName> <Level> <Restzeit>" },
                            { "Raidboss eintragen", prefix + "raids add <ArenaName> <PokemonName> <Restzeit>" },
                            { "Spezialfall: ArenaName mit mehreren Wörtern", prefix + "raids add <\"Arena Name\"> <PokemonName> <Restzeit>" },
                            { "Spezialfall: Noch nicht geschlüpften Raidboss eintragen", prefix + "raids add <ArenaName> <PokemonName> <RestzeitEi + 45>" },
                        }
                    }
                }
            };

            if (guild.Raids.Length > 0)
            {
                yield return raid;
            }

            var pokemon = new ModuleInfo
            {
                Module = "pokemon",
                Commands = { new CommandInfo
                    {
                        Command = "add",
                        Description = "Ermöglicht das hinzufügen von Pokemon auf der https://pg.festzeit.ch Karte",
                        Examples = new Dictionary<string, string>
                        {
                            { "Raid in Ei-Form eintragen", ".pokemon add <ArenaName> <Level> <Restzeit>" },
                            { "Raidboss eintragen", ".raids add <ArenaName> <PokemonName> <Restzeit>" },
                            { "Spezialfall: ArenaName mit mehreren Wörtern", ".raids add <\"Arena Name\"> <PokemonName> <Restzeit>" },
                            { "Spezialfall: Noch nicht geschlüpften Raidboss eintragen", ".raids add <ArenaName> <PokemonName> <RestzeitEi + 45>" },
                        }
                    }
                }
            };

            if (guild.Pokemon.Length > 0)
            {
                yield return pokemon;
            }
        }

        protected async Task ReplyEmbed(ModuleInfo info)
        {
            var message = new EmbedBuilder().WithTitle(string.Concat("**__Modul: ", info.Module, "__**"));
            foreach (var command in info.Commands)
            {
                message.AddField(string.Concat("**Command: ", command.Command, "**"), command.Description);
                foreach(var example in command.Examples)
                {
                    message.AddField(example.Key, example.Value);
                }
            }
                
            await ReplyAsync(string.Empty, embed: message.Build());
        }

        public class ModuleInfo
        {
            public ModuleInfo()
            {
                Commands = new List<CommandInfo>();
            }

            public string Module { get; set; }
            public List<CommandInfo> Commands { get; set; }
        }

        public class CommandInfo
        {
            public CommandInfo()
            {
                Examples = new Dictionary<string, string>();
            }

            public string Command { get; set; }
            public string Description { get; set; }
            public Dictionary<string, string> Examples { get; set; }
        }
    }
}