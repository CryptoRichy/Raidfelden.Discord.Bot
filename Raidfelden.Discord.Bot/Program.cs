﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Raidfelden.Discord.Bot.Services;
using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Runtime.InteropServices;
using Discord.Addons.Interactive;
using Raidfelden.Discord.Bot.Resources;
using Raidfelden.Data.Monocle;
using Raidfelden.Services;

namespace Raidfelden.Discord.Bot
{
    public class Program
    {
		private DiscordSocketClient _client;
		private CommandService _commands;

		public static IServiceProvider ServiceProvider { get; private set; }

        protected IConfigurationService ConfigurationService { get; set; }

        static void Main(string[] args)
        {
			new Program().RunBotAsync().GetAwaiter().GetResult();
        }

		public async Task RunBotAsync()
		{
            _client = new DiscordSocketClient();
			_commands = new CommandService();

            ConfigurationService = new ConfigurationService();
            var connectionString = ConfigurationService.GetConnectionString("ScannerDatabase");

            ServiceProvider = new ServiceCollection()
				.ConfigureMonocle(connectionString)
				.ConfigureServices()
                .AddSingleton(_client)
                .AddSingleton(_commands)
				.AddSingleton<IEmojiService, EmojiService>()
                .AddSingleton<InteractiveService>()
                .BuildServiceProvider();

            ConfigurationService = ServiceProvider.GetService<IConfigurationService>();

            var appConfiguration = ConfigurationService.GetAppConfiguration();
            var cultureInfo = new CultureInfo(appConfiguration.CultureCode);
            Thread.CurrentThread.CurrentUICulture = cultureInfo;
            i18n.Culture = cultureInfo;

            string botToken = appConfiguration.BotToken;
            // Event Subscriptions
            _client.Log += Log;

            _client.Disconnected += _client_DisconnectedAsync;

			await RegisterCommandsAsync();

			await _client.LoginAsync(TokenType.Bot, botToken);

			await _client.StartAsync();

			await Task.Delay(-1);
		}

        private async Task _client_DisconnectedAsync(Exception arg)
        {
			// This is probably not needed on windows machine, but on my mac server I get frequent disconnectes from which the bot can't recover
			// So I stop the bot with an exit code and wait for the sh script to restart the bot
	        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
	        if (!isWindows)
	        {
		        Console.WriteLine("Got a disconnect, closing the Bot and wait for the script to restart it");
		        Environment.Exit(1);
	        }
            await Task.CompletedTask;
        }

        private Task Log(LogMessage arg)
		{
			Console.WriteLine(arg);
			return Task.CompletedTask;
		}

		public async Task RegisterCommandsAsync()
		{
			_client.MessageReceived += HandleCommandAsync;
			await _commands.AddModulesAsync(Assembly.GetEntryAssembly());
		}

		private async Task HandleCommandAsync(SocketMessage arg)
		{
			var message = arg as SocketUserMessage;
			if (message == null || message.Author.IsBot) { return; }

			int argPos = 0;
            var context = new SocketCommandContext(_client, message);
            ulong? guildId = null;
            if (context.Guild != null)
            {
                guildId = context.Guild.Id;
            }
            var guildConfiguration = ConfigurationService.GetGuildConfiguration(guildId);
            var prefix = ConfigurationService.GetCommandPrefix(guildConfiguration);
            if (message.HasStringPrefix(prefix, ref argPos) || message.HasMentionPrefix(_client.CurrentUser, ref argPos))
			{
				
				var result = await _commands.ExecuteAsync(context, argPos, ServiceProvider);
				if (!result.IsSuccess)
				{
					Console.WriteLine(result.ErrorReason);
				}
			}

            var ocrChannels = ConfigurationService.GetChannelConfigurations(guildConfiguration, context.Channel.Name).Where(e => e.IsOcrAllowed).Select(e => e.Name.ToLowerInvariant()).ToArray();
            if ((ocrChannels.Contains(message.Channel.Name.ToLowerInvariant()) || message.Author.Username == "psewar") && message.Attachments.Count > 0)
            {
                var result = await _commands.ExecuteAsync(context, "raids ocr", ServiceProvider);
                if (!result.IsSuccess)
                {
                    Console.WriteLine(result.ErrorReason);
                }
            }
		}
	}
}
