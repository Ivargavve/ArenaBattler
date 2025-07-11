using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using backend.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using backend.Utils;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BattleController : ControllerBase
    {
        private readonly AppDbContext _db;

        public BattleController(AppDbContext db)
        {
            _db = db;
        }

        [HttpPost("turn")]
        public async Task<IActionResult> BattleTurn([FromBody] BattleRequest req)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                return Unauthorized();

            var player = await _db.Characters
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (player == null)
                return NotFound();

            if (player.CurrentEnergy <= 0)
                return BadRequest("Not enough energy to perform an attack.");

            var rand = new System.Random();
            var enemyTemplates = EnemyTemplates.All;

            EnemyTemplate selectedEnemy;
            int enemyHp;

            if (req.EnemyHp == null || req.EnemyHp == 0)
            {
                selectedEnemy = enemyTemplates[rand.Next(enemyTemplates.Count)];
                enemyHp = selectedEnemy.MaxHp;
            }
            else
            {
                var nameFromFrontend = req.EnemyName ?? "Enemy";
                selectedEnemy = EnemyTemplates.GetByName(nameFromFrontend) ?? enemyTemplates[0];
                enemyHp = req.EnemyHp ?? selectedEnemy.MaxHp;
            }

            var enemy = new
            {
                Name = selectedEnemy.Name,
                Hp = enemyHp,
                MaxHp = selectedEnemy.MaxHp,
                Attack = selectedEnemy.Attack,
                Defense = selectedEnemy.Defense,
                xp = selectedEnemy.XP,
                CritChance = selectedEnemy.CritChance
            };

            var log = new List<BattleLogEntry>();
            if (req.Action == "attack")
            {
                if ((enemy.Hp == enemy.MaxHp) && (player.CurrentHealth == player.MaxHealth))
                {
                    var encounterLines = BattleDialogLines.EncounterOpeners(player.Name, player.Level, enemy.Name.ToLower());
                    log.Add(new BattleLogEntry { Message = encounterLines[rand.Next(encounterLines.Length)], Type = "encounter" });
                }
                var playerOpeners = BattleDialogLines.PlayerOpeners(player.Name, enemy.Name.ToLower());
                log.Add(new BattleLogEntry { Message = playerOpeners[rand.Next(playerOpeners.Length)], Type = "player-attack" });

                int basePlayerDamage = Math.Max(player.Attack - enemy.Defense, 1);
                double rngFactor = 0.8 + rand.NextDouble() * 0.4;
                int rolledDamage = (int)System.Math.Round(basePlayerDamage * rngFactor);
                double critRoll = rand.NextDouble();
                bool isCrit = critRoll < player.CriticalChance;

                int playerDamage = rolledDamage;
                if (isCrit)
                {
                    playerDamage = (int)System.Math.Round((double)playerDamage * 2);
                    var critLines = BattleDialogLines.CritLines(player.Name, playerDamage);
                    log.Add(new BattleLogEntry { Message = critLines[rand.Next(critLines.Length)], Type = "player-crit" });
                }
                log.Add(new BattleLogEntry
                {
                    Message = $"{player.Name} deals {playerDamage} damage to the {enemy.Name.ToLower()}.",
                    Type = isCrit ? "player-crit-damage" : "player-attack-damage"
                });

                int enemyHpNew = enemy.Hp - playerDamage;
                var enemyHpLines = BattleDialogLines.EnemyHpLines(enemy.Name, System.Math.Max(enemyHpNew, 0), enemy.MaxHp);
                log.Add(new BattleLogEntry { Message = enemyHpLines[rand.Next(enemyHpLines.Length)], Type = "enemy-hp" });

                if (enemyHpNew <= 0)
                {
                    var victoryLines = BattleDialogLines.VictoryLines(player.Name, enemy.Name.ToLower());
                    log.Add(new BattleLogEntry { Message = victoryLines[rand.Next(victoryLines.Length)], Type = "victory" });
                    log.Add(new BattleLogEntry { Message = $"{player.Name} gains {enemy.xp} XP from the battle.", Type = "xp" });
                    player.CurrentEnergy -= 1;

                    int gainedXp = enemy.xp;
                    player.ExperiencePoints += gainedXp;

                    if (player.ExperiencePoints >= 100)
                    {
                        player.ExperiencePoints = 0;
                        player.Level += 1;
                        player.MaxHealth += 5;
                        player.CurrentHealth = player.MaxHealth;
                        player.Attack += 2;
                        player.Defense += 1;
                        player.CriticalChance += 0.01;
                        log.Add(new BattleLogEntry { Message = $"🎉 {player.Name} has reached level {player.Level}!", Type = "levelup" });

                        if (player.User != null)
                        {
                            player.User.ExperiencePoints += 10;
                            if (player.User.ExperiencePoints >= 100)
                            {
                                player.User.ExperiencePoints = 0;
                                player.User.Level += 1;
                                player.User.Credits += 100;
                                log.Add(new BattleLogEntry { Message = $"🎉 Your account has reached user level {player.User.Level}!", Type = "user-levelup" });
                            }
                        }
                    }
                    await _db.SaveChangesAsync();

                    log.Add(new BattleLogEntry
                    {
                        Message = $"Status: {player.Name} is now Level {player.Level}, XP: {player.ExperiencePoints}/100, Energy: {player.CurrentEnergy}/{player.MaxEnergy}",
                        Type = "status"
                    });
                    log.Add(new BattleLogEntry
                    {
                        Message = $"{player.Name}: {player.CurrentHealth}/{player.MaxHealth} HP | {enemy.Name}: 0/{enemy.MaxHp} HP",
                        Type = "hp-row"
                    });

                    return Ok(new
                    {
                        PlayerHp = player.CurrentHealth,
                        PlayerMaxHp = player.MaxHealth,
                        EnemyHp = 0,
                        EnemyMaxHp = enemy.MaxHp,
                        EnemyName = enemy.Name,
                        BattleLog = log,
                        BattleEnded = true,
                        GainedXp = gainedXp,
                        NewExperiencePoints = player.ExperiencePoints,
                        PlayerLevel = player.Level,
                        UserXp = player.User?.ExperiencePoints ?? 0,
                        UserLevel = player.User?.Level ?? 0,
                        PlayerEnergy = player.CurrentEnergy
                    });
                }

                var enemyActions = BattleDialogLines.EnemyActions(enemy.Name.ToLower());
                log.Add(new BattleLogEntry { Message = enemyActions[rand.Next(enemyActions.Length)], Type = "enemy-action" });

                var enemyAttackLines = BattleDialogLines.EnemyAttackLines(enemy.Name.ToLower());
                log.Add(new BattleLogEntry { Message = enemyAttackLines[rand.Next(enemyAttackLines.Length)], Type = "enemy-attack" });

                int baseEnemyDamage = System.Math.Max(enemy.Attack - player.Defense, 1);
                double enemyRng = 0.8 + rand.NextDouble() * 0.4;
                int enemyRolledDamage = (int)System.Math.Round(baseEnemyDamage * enemyRng);
                double enemyCritRoll = rand.NextDouble();
                bool enemyCrit = enemyCritRoll < enemy.CritChance;

                int enemyDamage = enemyRolledDamage;
                if (enemyCrit)
                {
                    enemyDamage = (int)System.Math.Round((double)enemyDamage * 2);
                    var enemyCritLines = BattleDialogLines.EnemyCritLines(enemy.Name.ToLower(), enemyDamage);
                    log.Add(new BattleLogEntry { Message = enemyCritLines[rand.Next(enemyCritLines.Length)], Type = "enemy-crit" });
                }

                log.Add(new BattleLogEntry
                {
                    Message = $"{player.Name} takes {enemyDamage} damage from the attack.",
                    Type = enemyCrit ? "enemy-crit-damage" : "enemy-attack-damage"
                });

                int playerHpNew = player.CurrentHealth - enemyDamage;
                var playerHpLines = BattleDialogLines.PlayerHpLines(player.Name, System.Math.Max(playerHpNew, 0), player.MaxHealth);
                log.Add(new BattleLogEntry { Message = playerHpLines[rand.Next(playerHpLines.Length)], Type = "player-hp" });

                player.CurrentHealth = System.Math.Max(0, playerHpNew);

                if (playerHpNew <= 0)
                {
                    var defeatLines = BattleDialogLines.DefeatLines(player.Name, enemy.Name.ToLower());
                    log.Add(new BattleLogEntry { Message = defeatLines[rand.Next(defeatLines.Length)], Type = "defeat" });
                    player.CurrentHealth = 0;
                    player.CurrentEnergy = 0;
                    player.ExperiencePoints = 0;
                    player.Level = 1;
                    await _db.SaveChangesAsync();

                    log.Add(new BattleLogEntry
                    {
                        Message = $"Status: {player.Name} has reset to Level {player.Level}, XP: {player.ExperiencePoints}, Energy: {player.CurrentEnergy}/{player.MaxEnergy}",
                        Type = "status"
                    });

                    log.Add(new BattleLogEntry
                    {
                        Message = $"{player.Name}: 0/{player.MaxHealth} HP | {enemy.Name}: {enemyHpNew}/{enemy.MaxHp} HP",
                        Type = "hp-row"
                    });

                    return Ok(new
                    {
                        PlayerHp = 0,
                        PlayerMaxHp = player.MaxHealth,
                        EnemyHp = enemyHpNew,
                        EnemyMaxHp = enemy.MaxHp,
                        EnemyName = enemy.Name,
                        BattleLog = log,
                        BattleEnded = true,
                        GainedXp = 0,
                        NewExperiencePoints = player.ExperiencePoints,
                        PlayerLevel = player.Level,
                        UserXp = player.User?.ExperiencePoints ?? 0,
                        UserLevel = player.User?.Level ?? 0,
                        PlayerEnergy = player.CurrentEnergy
                    });
                }

                await _db.SaveChangesAsync();
                log.Add(new BattleLogEntry
                {
                    Message = $"{player.Name}: {player.CurrentHealth}/{player.MaxHealth} HP | {enemy.Name}: {enemyHpNew}/{enemy.MaxHp} HP",
                    Type = "hp-row"
                });

                log.Add(new BattleLogEntry
                {
                    Message = "--- End of turn ---",
                    Type = "turn-end"
                });

                return Ok(new
                {
                    PlayerHp = playerHpNew,
                    PlayerMaxHp = player.MaxHealth,
                    EnemyHp = enemyHpNew,
                    EnemyMaxHp = enemy.MaxHp,
                    EnemyName = enemy.Name,
                    BattleLog = log,
                    BattleEnded = false,
                    GainedXp = 0,
                    NewExperiencePoints = player.ExperiencePoints,
                    PlayerLevel = player.Level,
                    UserXp = player.User?.ExperiencePoints ?? 0,
                    UserLevel = player.User?.Level ?? 0,
                    PlayerEnergy = player.CurrentEnergy
                });
            }
            return BadRequest("Unknown action");
        }

        [HttpGet("encounter")]
        public IActionResult EncounterEnemy()
        {
            var rand = new System.Random();
            var enemyTemplates = EnemyTemplates.All;
            var selectedEnemy = enemyTemplates[rand.Next(enemyTemplates.Count)];

            return Ok(new
            {
                enemyName = selectedEnemy.Name,
                enemyHp = selectedEnemy.MaxHp,
                enemyMaxHp = selectedEnemy.MaxHp,
            });
        }
    }
    public class BattleLogEntry
    {
        public string Message { get; set; } = "";
        public string Type { get; set; } = ""; 
    }
}
