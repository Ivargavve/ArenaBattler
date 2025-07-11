import { Component, OnInit, ViewChild, ElementRef, AfterViewInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Fighter, BattleLogEntry } from '../../services/battle-interfaces';
import { AuthService } from '../../services/auth.service';
import { environment } from '../../../environments/environment';
import { Subscription, firstValueFrom } from 'rxjs';
import { Router } from '@angular/router';
import { BattleService } from '../../services/battle.service';

@Component({
  selector: 'app-battle',
  standalone: true,
  imports: [ CommonModule ],
  templateUrl: './battle-component.html',
  styleUrl: './battle-component.scss'
})
export class BattleComponent implements OnInit, AfterViewInit, OnDestroy {
  player: Fighter | null = null;
  enemy: Fighter | null = null;
  battleLog: BattleLogEntry[] = [];
  isLoading = false; 
  battleEnded = false;
  gainedXp: number | null = null;
  userLevel: number | null = null;
  userXp: number | null = null;
  playerEnergy: number = 0;
  enemyName: string | null = null;

  attacks = [
    { 
      name: 'Standard Attack',
      currentCharges: 18,
      maxCharges: 20,
      disabled: false,
      action: () => this.attack()
    },
    { 
      name: 'Special Attack',
      currentCharges: 11,
      maxCharges: 20,
      disabled: true,
      action: null
    },
    {
      name: 'Defend',
      currentCharges: 9,
      maxCharges: 10,
      disabled: true,
      action: null
    },
    {
      name: 'Fourth Move',
      currentCharges: 2,
      maxCharges: 5,
      disabled: true,
      action: null
    }
  ];

  private characterSub!: Subscription;

  @ViewChild('battleLogContainer') battleLogContainer!: ElementRef;

  constructor(
    private http: HttpClient,
    private authService: AuthService,
    private router: Router,
    private battleService: BattleService
  ) {}

  ngOnInit(): void {
    this.characterSub = this.authService.character$.subscribe(character => {
      if (character) {
        this.player = {
          name: character.name,
          hp: character.currentHealth,
          maxHp: character.maxHealth
        };
        this.playerEnergy = character.currentEnergy;
        this.userLevel = character.level;
        this.userXp = character.experiencePoints;
      } else {
        this.player = null;
        this.playerEnergy = 0;
        this.userLevel = null;
        this.userXp = null;
      }
    });
    // Ladda eventuell sparad battle-state via service
    const loadedState = this.battleService.loadBattleState();
    if (loadedState) {
      this.player = loadedState.player;
      this.enemy = loadedState.enemy;
      this.battleLog = loadedState.battleLog || [];
      this.battleEnded = loadedState.battleEnded;
      this.enemyName = loadedState.enemyName;
      this.userLevel = loadedState.userLevel;
      this.userXp = loadedState.userXp;
      this.playerEnergy = loadedState.playerEnergy;
      // Lägg till fler properties om du har!
    } else {
      this.battleLog = [];
      this.battleEnded = true;
      this.startNewBattle();
    }
  }

  ngAfterViewInit(): void {
    this.scrollToBottom();
  }

  ngOnDestroy(): void {
    if (this.characterSub) {
      this.characterSub.unsubscribe();
    }
  }

  scrollToBottom(): void {
    setTimeout(() => {
      if (this.battleLogContainer && this.battleLogContainer.nativeElement) {
        this.battleLogContainer.nativeElement.scrollTo({
          top: this.battleLogContainer.nativeElement.scrollHeight,
          behavior: 'smooth'
        });
      }
    }, 30);
  }

  startNewBattle(): void {
    this.battleService.setInBattle(true);
    if (this.playerEnergy > 0) {
      this.isLoading = true;
      this.battleEnded = false;
      this.enemyName = null;
      this.enemy = null;
      this.battleLog = [{ message: "Starting new battle...", type: "start" }];
      this.scrollToBottom();

      this.http.get<any>(`${environment.apiUrl}/battle/encounter`).subscribe({
        next: (enemyData) => {
          this.enemyName = enemyData.enemyName;
          this.enemy = {
            name: enemyData.enemyName,
            hp: enemyData.enemyHp,
            maxHp: enemyData.enemyMaxHp
          };
          this.battleLog.push({ message: `You encounter a wild ${this.enemyName}! Prepare for battle!`, type: "encounter" });
          this.isLoading = false;
          this.scrollToBottom();
          this.saveBattleState();
        },
        error: (err) => {
          this.battleLog = [{ message: "ERROR: " + (err.error?.message || err.statusText), type: "error" }];
          this.isLoading = false;
          this.scrollToBottom();
          this.saveBattleState();
        }
      });

    } else {
      this.battleLog.push({ message: "You have no energy left to battle! Please rest or visit the shop.", type: "info" });
      this.battleEnded = true;
      this.scrollToBottom();
      this.saveBattleState();
    }
  }

  attack(): void {
    if (!this.player || !this.enemy || this.battleEnded || this.playerEnergy <= 0) return;

    this.isLoading = true;

    const req = {
      enemyHp: this.enemy.hp,
      enemyName: this.enemy.name, 
      action: 'attack'
    };

    this.http.post<any>(`${environment.apiUrl}/battle/turn`, req)
      .subscribe({
        next: async (res) => {
          this.player!.hp = res.playerHp;
          this.player!.maxHp = res.playerMaxHp;
          this.enemy!.hp = res.enemyHp;
          this.enemy!.maxHp = res.enemyMaxHp;
          this.enemy!.name = res.enemyName; 
          this.enemyName = res.enemyName;
          this.battleLog.push(...res.battleLog);
          this.battleEnded = res.battleEnded;

          if (res.battleEnded && this.player!.hp > 0) {
            this.gainedXp = res.gainedXp ?? null;
            this.onBattleEnd();
          } else if (res.battleEnded && this.player!.hp <= 0) {
            this.isLoading = true;
            try {
              await firstValueFrom(this.http.delete(`${environment.apiUrl}/characters`));
            } catch {}
            this.isLoading = false;
            this.gainedXp = null;
            this.onBattleEnd();
          } 
          else {
            this.gainedXp = null;
          }
          this.authService.loadUserWithCharacter();
          this.isLoading = false;
          this.scrollToBottom();
          this.saveBattleState();
        },
        error: (err) => {
          this.battleLog.push({ message: "ERROR: " + (err.error?.message || err.statusText), type: "error" });
          this.isLoading = false;
          this.scrollToBottom();
          this.saveBattleState();
        }
      });
  }

  private resetBattleAndNavigate(): void {
    try {
      this.saveBattleState();              
      this.battleService.setInBattle(false);
      this.battleService.clearBattleState();
      this.player = null;
      this.enemy = null;
      this.battleLog = [];
      this.battleEnded = true;
      this.gainedXp = null;
      this.enemyName = null;
      this.userLevel = null;
      this.userXp = null;
      this.playerEnergy = 0;
    } catch (err) {} finally {
      this.isLoading = false;
      this.router.navigate(['/battle-planner']);
    }
  }

  onBattleEnd() {
    setTimeout(() => {
      this.resetBattleAndNavigate();
    }, 4000);
  }

  runFromBattle() {
    if (this.playerEnergy > 0) {
      this.isLoading = true;
      this.authService.useEnergy(1).then(() => {
        this.resetBattleAndNavigate();
      }).catch(() => {
        this.resetBattleAndNavigate();
      });
    } else {
      this.resetBattleAndNavigate();
    }
  }

  saveBattleState() {
    this.battleService.saveBattleState({
      player: this.player,
      enemy: this.enemy,
      battleLog: this.battleLog,
      battleEnded: this.battleEnded,
      enemyName: this.enemyName,
      userLevel: this.userLevel,
      userXp: this.userXp,
      playerEnergy: this.playerEnergy
    });
  }

  getLogClass(log: BattleLogEntry): string {
    switch (log.type) {
      case 'victory':        return 'battle-victory';
      case 'defeat':         return 'battle-defeat';
      case 'player-crit':    return 'battle-crit';
      case 'enemy-crit':     return 'battle-crit-enemy';
      case 'player-attack-damage': return 'battle-friendly-damage';
      case 'enemy-attack-damage':  return 'battle-enemy-damage';
      case 'player-crit-damage':   return 'battle-crit';
      case 'enemy-crit-damage':    return 'battle-crit-enemy';
      case 'status':         return 'battle-status';
      case 'turn-end':       return 'battle-divider';
      case 'hp-row':         return 'battle-hp-row';
      case 'xp':             return 'battle-xp';
      case 'levelup':        return 'battle-levelup';
      case 'user-levelup':   return 'battle-user-levelup';
      case 'enemy-hp':       return 'battle-enemy-hp';
      case 'player-hp':      return 'battle-player-hp';
      case 'encounter':      return 'battle-encounter';
      default:               return '';
    }
  }
}
