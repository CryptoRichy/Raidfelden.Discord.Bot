﻿using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Raidfelden.Discord.Bot.Monocle;
using Raidfelden.Discord.Bot.Models;
using Raidfelden.Discord.Bot.Configuration;
using System.Collections.Generic;

namespace Raidfelden.Discord.Bot.Services
{
    public interface IRaidService
    {
        Task<ServiceResponse> AddAsync(string gymName, string pokemonNameOrLevel, string timeLeft, int interactiveLimit, IEnumerable<FenceConfiguration> fences);
        Task<ServiceResponse> HatchAsync(string gymName, string pokemonName, int interactiveLimit, IEnumerable<FenceConfiguration> fences);
    }

    public class RaidService : IRaidService
    {
        protected IGymService GymService { get; }
        protected IPokemonService PokemonService { get; }
        protected IRaidbossService RaidbossService { get; }

        public RaidService(IGymService gymService, IPokemonService pokemonService, IRaidbossService raidbossService)
        {
            GymService = gymService;
            PokemonService = pokemonService;
            RaidbossService = raidbossService;
        }

        #region Add

        public async Task<ServiceResponse> AddAsync(string gymName, string pokemonNameOrLevel, string timeLeft, int interactiveLimit, IEnumerable<FenceConfiguration> fences)
        {
            var startEndTime = GetStartEndDateTime(timeLeft);
            if (!startEndTime.HasValue) { return new ServiceResponse(false, "Die Restzeit muss im Format mm oder mm:ss angegeben werden."); }

            return await AddResolvePokemonOrLevelAsync(gymName, pokemonNameOrLevel, startEndTime.Value, interactiveLimit, fences);
        }

        private async Task<ServiceResponse> AddResolvePokemonOrLevelAsync(string gymName, string pokemonNameOrLevel, DateTime startEndTime, int interactiveLimit, IEnumerable<FenceConfiguration> fences)
        {
            if (int.TryParse(pokemonNameOrLevel, out int raidLevel))
            {
                if (raidLevel < 1)
                {
                    return new ServiceResponse(false, "Der kleinste zulässige Wert für einen Raid-Level beträgt 1.");
                }
                if (raidLevel > 5)
                {
                    return new ServiceResponse(false, "Der grösste zulässige Wert für einen Raid-Level beträgt 5.");
                }
                return await AddResolveGymAsync(gymName, Convert.ToByte(raidLevel), null, null, startEndTime, interactiveLimit, fences);
            }

            var pokemonResponse = await PokemonService.GetPokemonAndRaidbossAsync(pokemonNameOrLevel, interactiveLimit, (selectedPokemonName) => AddResolvePokemonOrLevelAsync(gymName, selectedPokemonName, startEndTime, interactiveLimit, fences));
            if (!pokemonResponse.IsSuccess) { return pokemonResponse; }

            var pokemonAndRaidboss = pokemonResponse.Result;
            var pokemon = pokemonAndRaidboss.Key;
            var raidboss = pokemonAndRaidboss.Value;
            return await AddResolveGymAsync(gymName, Convert.ToByte(raidboss.Level), pokemon, raidboss, startEndTime, interactiveLimit, fences);
        }

        private async Task<ServiceResponse> AddResolveGymAsync(string gymName, byte level, IPokemon pokemon, IRaidboss raidboss, DateTime startEndTime, int interactiveLimit, IEnumerable<FenceConfiguration> fences)
        {
            using (var context = new Hydro74000Context())
            {
                var gymResponse = await GymService.GetGymAsync(context, gymName, interactiveLimit, (selectedGymId) => AddResolveGymAsync(selectedGymId, level, pokemon, raidboss, startEndTime, interactiveLimit, fences), fences);
                if (!gymResponse.IsSuccess) { return gymResponse; }

                return await AddSaveAsync(context, gymResponse.Result, level, pokemon, raidboss, startEndTime);
            }
        }

        private async Task<ServiceResponse> AddResolveGymAsync(int gymId, byte level, IPokemon pokemon, IRaidboss raidboss, DateTime startEndTime, int interactiveLimit, IEnumerable<FenceConfiguration> fences)
        {
            using (var context = new Hydro74000Context())
            {
                var gym = await context.Forts.SingleAsync(e => e.Id == gymId);
                return await AddSaveAsync(context, gym, level, pokemon, raidboss, startEndTime);
            }
        }

        private async Task<ServiceResponse> AddSaveAsync(Hydro74000Context context, Forts gym, byte level, IPokemon pokemon, IRaidboss raidboss, DateTime startEndTime)
        {
            var localDateTime = new LocalDateTime(startEndTime.Year, startEndTime.Month, startEndTime.Day, startEndTime.Hour, startEndTime.Minute, startEndTime.Second);
            var expiry = new ZonedDateTime(localDateTime, DateTimeZone.Utc, Offset.Zero).ToInstant();

            // Create the raid entry
            var isNewRaid = false;
            var beforeSpawnTime = SystemClock.Instance.GetCurrentInstant().Minus(Duration.FromMinutes(90)).ToUnixTimeSeconds();
            var raid = context.Raids.FirstOrDefault(e => e.FortId == gym.Id && e.TimeSpawn > beforeSpawnTime);
            if (raid == null)
            {
                isNewRaid = true;
                raid = new Raids
                {
                    ExternalId = ThreadLocalRandom.NextLong(),
                    Fort = gym,
                    FortId = gym.Id
                };
                context.Raids.Add(raid);
            }

            string message;
            if (raidboss == null)
            {
                raid.Level = level;
                raid.TimeSpawn = (int)expiry.Minus(Duration.FromMinutes(60)).ToUnixTimeSeconds();
                raid.TimeBattle = (int)expiry.ToUnixTimeSeconds();
                raid.TimeEnd = (int)expiry.Plus(Duration.FromMinutes(45)).ToUnixTimeSeconds();
                message = $"Neuer Level {level} Raid an {gym.Name} (beginnt ca um {expiry.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}) eingetragen.";
            }
            else
            {
                raid.PokemonId = (short)raidboss.Id;
                raid.Level = level;
                raid.TimeSpawn = (int)expiry.Minus(Duration.FromMinutes(105)).ToUnixTimeSeconds();
                raid.TimeBattle = (int)expiry.Minus(Duration.FromMinutes(45)).ToUnixTimeSeconds();
                raid.TimeEnd = (int)expiry.ToUnixTimeSeconds();
                if (isNewRaid)
                {
                    message = $"Neuer Raidboss {pokemon.Name} an {gym.Name} (endet ca um {expiry.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}) eingetragen.";
                }
                else
                {
                    message = $"Raidboss {pokemon.Name} an {gym.Name} (endet ca um {expiry.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} verändert.";
                }
            }

            await context.SaveChangesAsync();
            return new ServiceResponse(true, message);
        }

        private DateTime? GetStartEndDateTime(string timeLeft)
        {
            var timeParts = timeLeft.Split(":", StringSplitOptions.RemoveEmptyEntries);
            int secondsToAdd;
            switch (timeParts.Length)
            {
                case 2:
                    var minutes = int.Parse(timeParts[0]);
                    var seconds = int.Parse(timeParts[1]);
                    secondsToAdd = minutes * 60 + seconds;
                    break;
                case 1:
                    var minutesOnly = int.Parse(timeParts[0]);
                    secondsToAdd = minutesOnly * 60;
                    break;
                default:
                    return null;
            }
            return DateTime.Now.AddSeconds(secondsToAdd);
        }

        #endregion

        #region Hatch

        public async Task<ServiceResponse> HatchAsync(string gymName, string pokemonName, int interactiveLimit, IEnumerable<FenceConfiguration> fences)
        {
            var pokemonResponse = await PokemonService.GetPokemonAndRaidbossAsync(pokemonName, interactiveLimit, (selectedPokemonName) => HatchAsync(gymName, selectedPokemonName, interactiveLimit, fences));
            if (!pokemonResponse.IsSuccess) { return pokemonResponse; }

            var pokemonAndRaidboss = pokemonResponse.Result;
            var pokemon = pokemonAndRaidboss.Key;
            var raidboss = pokemonAndRaidboss.Value;

            return await HatchResolveGymAsync(gymName, pokemon, raidboss, interactiveLimit, fences);
        }

        public async Task<ServiceResponse> HatchResolveGymAsync(string gymName, IPokemon pokemon, IRaidboss raidboss, int interactiveLimit, IEnumerable<FenceConfiguration> fences)
        {
            using (var context = new Hydro74000Context())
            {
                var gymResponse = await GymService.GetGymAsync(context, gymName, interactiveLimit, (selectedGymId) => HatchResolveGymWithIdAsync(selectedGymId, pokemon, raidboss, interactiveLimit), fences);
                if (!gymResponse.IsSuccess) { return gymResponse; }

                return await HatchSaveAsync(context, gymResponse.Result, pokemon, raidboss, interactiveLimit);
            }
        }

        public async Task<ServiceResponse> HatchResolveGymWithIdAsync(int gymId, IPokemon pokemon, IRaidboss raidboss, int interactiveLimit)
        {
            using (var context = new Hydro74000Context())
            {
                var gym = await context.Forts.SingleAsync(e => e.Id == gymId);
                return await HatchSaveAsync(context, gym, pokemon, raidboss, interactiveLimit);
            }
        }

        public async Task<ServiceResponse> HatchSaveAsync(Hydro74000Context context, Forts gym, IPokemon pokemon, IRaidboss raidboss, int interactiveLimit)
        {
            var beforeSpawnTime = SystemClock.Instance.GetCurrentInstant().Minus(Duration.FromMinutes(90)).ToUnixTimeSeconds();
            var raid = context.Raids.FirstOrDefault(e => e.FortId == gym.Id && e.TimeSpawn > beforeSpawnTime);
            if (raid == null)
            {
                return new ServiceResponse(false, $"Momentan ist kein Raid an der Arena \"{gym.Name}\" eingetragen.");
            }

            raid.PokemonId = (short)raidboss.Id;
            await context.SaveChangesAsync();
            return new ServiceResponse(true, $"{pokemon.Name} ist nun der neue Raidboss bei der Arena \"{gym.Name}\".");
        }

        #endregion
    }
}