using Helpers;
using SandBox;
using SandBox.Source.Missions;
using SandBox.TournamentMissions.Missions;
using SandBox.View;
using SandBox.View.Missions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.LogEntries;
using TaleWorlds.CampaignSystem.SandBox.GameComponents;
using TaleWorlds.CampaignSystem.SandBox.Source.TournamentGames;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.LegacyGUI.Missions;
using TaleWorlds.MountAndBlade.Source.Missions;
using TaleWorlds.MountAndBlade.View.Missions;
using TaleWorlds.ObjectSystem;
using TaleWorlds.SaveSystem;
using static TaleWorlds.CampaignSystem.CampaignBehaviorBase;

namespace Jousting
{
    public class MySubModule:MBSubModuleBase
    {
        
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            CampaignGameStarter campaignGameStarter;
            var campaign = game.GameType as Campaign;
            if (campaign != null)
            {
                campaignGameStarter = (CampaignGameStarter)gameStarterObject;
                campaignGameStarter.AddBehavior( new JoustingTournamentBehavior());
            }

        }
        
    }

    public class JoustingTournamentBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, new Action<CampaignGameStarter>(this.OnSessionLaunched));
        }
        public override void SyncData(IDataStore dataStore)
        {
        }

        public void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
        {
            this.AddGameMenus(campaignGameStarter);
        }
        protected void AddGameMenus(CampaignGameStarter campaignGameSystemStarter)
        {
            campaignGameSystemStarter.AddGameMenuOption("menu_town_tournament_join", "mno_tournament_jousting", "Join a jousting duel", new GameMenuOption.OnConditionDelegate(this.Game_menu_tournament_jousting_on_condition), new GameMenuOption.OnConsequenceDelegate(this.Game_menu_tournament_jousting_on_consequence), false, -1, false);
        }
        private bool Game_menu_tournament_jousting_on_condition(MenuCallbackArgs args)
        {
            bool disableOption;
            TextObject disabledText;
            bool canPlayerDo = Campaign.Current.Models.SettlementAccessModel.CanMainHeroDoSettlementAction(Settlement.CurrentSettlement, SettlementAccessModel.SettlementAction.JoinTournament, out disableOption, out disabledText);
            args.optionLeaveType = GameMenuOption.LeaveType.HostileAction;
            return MenuHelper.SetOptionProperties(args, canPlayerDo, disableOption, disabledText);
        }

        private void Game_menu_tournament_jousting_on_consequence(MenuCallbackArgs args)
        {

            TournamentGame tournamentGame = new JoustingTournamentGame(Settlement.CurrentSettlement.Town);
            GameMenu.SwitchToMenu("town");
            tournamentGame.PrepareForTournamentGame(true);
            Campaign.Current.TournamentManager.OnPlayerJoinTournament(tournamentGame.GetType(), Settlement.CurrentSettlement);

        }
    }

    public class JoustingTournamentModel: DefaultTournamentModel
    {
        public override TournamentGame CreateTournament(Town town)
        {
            return new JoustingTournamentGame(town);
        }
        
    }
    
    [SaveableClass(20117)]
    public class JoustingTournamentGame: TournamentGame
    {
        public JoustingTournamentGame(Town town, ItemObject prize = null): base(town, prize)
        {
            
        }
        public override int MaxTeamNumberPerMatch { get { return 2; } }
        public override int MaxTeamSize { get { return 1; } }


        public override TextObject GetMenuText()
        {
            throw new NotImplementedException();
        }
        public override void OpenMission(Settlement settlement, bool isPlayerParticipating)
        {
            var upgradeLevel = settlement.IsTown ? settlement.GetComponent<Town>().GetWallLevel() : 1;
            ModMissions.OpenTournamentJoustingMission("empire_jousting_arena"/*LocationComplex.Current.GetScene(CampaignData.LocationArena, upgradeLevel)*/, this, settlement, isPlayerParticipating);
        }
    }

    [MissionManager]
    public static class ModMissions
    {
        //place to use behaviours, place to create the mission.
        [MissionMethod]
        public static Mission OpenTournamentJoustingMission(string scene, JoustingTournamentGame tournamentGame, Settlement settlement, bool isPlayerParticipating)
        {
            return MissionState.OpenNew
            (
                "TournamentJousting2",
                SandBoxMissions.CreateSandBoxMissionInitializerRecord(scene),
                missionController =>
                {
                    var joustingGameBehavior = new JoustingTournamentMissionController();
                    return new MissionBehaviour[]
                    {
                    new CampaignMissionComponent(),
                    joustingGameBehavior,
                    new TournamentBehavior(tournamentGame, settlement, joustingGameBehavior, isPlayerParticipating),
                    new AgentVictoryLogic(),
                    new AgentBattleAILogic(),
                    new ArenaAgentStateDeciderLogic(),
                    new MissionHardBorderPlacer(),
                    new MissionBoundaryPlacer(),
                    new MissionOptionsComponent(),
                    new HighlightsController(),
                    new SandboxHighlightsController(),
                    };
                }
            );
        }
    }

    //place to spawn people, give them clothes, etc.
    public class JoustingTournamentMissionController : MissionLogic, ITournamentGameBehavior
    {
        public static int WinningPoint = 3;
        public static float MaxDistanceDifference = 1.5f;

        private TournamentMatch _match;
        private bool _isLastRound;
        private BasicTimer _endTimer;
        private BasicTimer _cheerTimer;
        private List<GameEntity> _spawnPoints;
        private List<GameEntity> _wayPoints;
        private List<GameEntity> _volumeBoxes;
        private bool _isSimulated;
        private bool _forceEndMatch;
        //private CultureObject _culture;
        private List<TournamentParticipant> _aliveParticipants;
        private List<TournamentTeam> _aliveTeams;
        private WinnerAgent _winnerAgent = WinnerAgent.notDecidedYet;
        private float _winningDecounter;
        private Agent _firstFighter;
        private Agent _secondFighter;
        private TournamentParticipant _firstParticipant;
        private TournamentParticipant _secondParticipant;
        private DuelState _duelState;
        private float _countdownValue;
        private int _countdownTurn = 3;
        private bool _firstCouchModeActivated;
        private bool _secondCouchModeActivated;
        private bool _firstFighterDestinationReached;
        private bool _secondFighterDestinationReached;
        private float _prevFirstFighterDistanceDifference;
        private float _prevSecondFighterDistanceDifference;
        private float _distanceDifferenceCountDown; 

        enum DuelState
        {
            firstCountdownState,
            secondCountdownState,
            firstState,
            firstGettingReadyState,
            secondState,
            secondGettingReadyState
        }

        enum WinnerAgent
        {
            notDecidedYet,
            firstAgent,
            secondAgent
        }
        public JoustingTournamentMissionController()
        {
            this._match = (TournamentMatch)null;
            _duelState = DuelState.firstCountdownState;
            _firstCouchModeActivated = false;
            _secondCouchModeActivated = false;
            _firstFighterDestinationReached = false;
            _secondFighterDestinationReached = false;
            _winningDecounter = 0;
            _distanceDifferenceCountDown = 0;
        }

        private ItemObject GetJoustingWeapon()
        {
            return MBObjectManager.Instance.GetObject<ItemObject>("vlandia_sword_1_t2"); //vlandia_lance_1_t3
        }

        
        public bool IsMatchEnded()
        {
            if (this._isSimulated || this._match == null || (!(_winnerAgent == WinnerAgent.notDecidedYet) && _winningDecounter > 3))
            {
                
                return true;
            }

            if(this._endTimer != null && (double)this._endTimer.ElapsedTime > 6.0 || this._forceEndMatch)
            {
                this._forceEndMatch = false;
                this._endTimer = (BasicTimer)null;
                return true;
            }
            if (this._cheerTimer != null && (double)this._cheerTimer.ElapsedTime > 1.0)
            {
                this.OnMatchResultsReady();
                this._cheerTimer = (BasicTimer)null;
                AgentVictoryLogic missionBehaviour = (AgentVictoryLogic)((MissionBehaviour)this).Mission.GetMissionBehaviour<AgentVictoryLogic>();
                using (IEnumerator<Agent> enumerator = ((IEnumerable<Agent>)((MissionBehaviour)this).Mission.Agents).GetEnumerator())
                {
                    while (((IEnumerator)enumerator).MoveNext())
                    {
                        Agent current = enumerator.Current;
                        if (current.IsAIControlled)
                            missionBehaviour.SetTimersOfVictoryReactions(current, 1f, 3f);
                    }
                }
                return false;
            }
            if (this._endTimer == null && !this.CheckIfIsThereAnyEnemies())
            {
                this._endTimer = new BasicTimer((MBCommon.TimeType)1);
                this._cheerTimer = new BasicTimer((MBCommon.TimeType)1);
            }
            return false;

        }

        public bool CheckIfIsThereAnyEnemies()
        {
            Team team = (Team)null;
            using (IEnumerator<Agent> enumerator = ((IEnumerable<Agent>)((MissionBehaviour)this).Mission.Agents).GetEnumerator())
            {
                while (((IEnumerator)enumerator).MoveNext())
                {
                    Agent current = enumerator.Current;
                    if (current.IsHuman && current.Team != null)
                    {
                        if (team == null)
                            team = current.Team;
                        else if (team != current.Team)
                            return true;
                    }
                }
            }
            return false;
        }

        public void OnMatchResultsReady()
        {
            if (this._match.IsPlayerParticipating())
            {
                if (this._match.IsPlayerWinner())
                {
                    if (this._isLastRound)
                    {
                        if (this._match.QualificationMode == TournamentGame.QualificationMode.IndividualScore)
                            InformationManager.AddQuickInformation(new TextObject("{=Jn0k20c3}Round is over, you survived the final round of the tournament.", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");
                        else
                            InformationManager.AddQuickInformation(new TextObject("{=wOqOQuJl}Round is over, your team survived the final round of the tournament.", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");
                    }
                    else if (this._match.QualificationMode == TournamentGame.QualificationMode.IndividualScore)
                        InformationManager.AddQuickInformation(new TextObject("{=uytwdSVH}Round is over, you are qualified for the next stage of the tournament.", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");
                    else
                        InformationManager.AddQuickInformation(new TextObject("{=fkOYvnVG}Round is over, your team is qualified for the next stage of the tournament.", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");
                }
                else if (this._match.QualificationMode == TournamentGame.QualificationMode.IndividualScore)
                    InformationManager.AddQuickInformation(new TextObject("{=lcVauEKV}Round is over, you are disqualified from the tournament.", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");
                else
                    InformationManager.AddQuickInformation(new TextObject("{=MLyBN51z}Round is over, your team is disqualified from the tournament.", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");
            }
            else
                InformationManager.AddQuickInformation(new TextObject("{=UBd0dEPp}Match is over", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");
        }

        
        
        public void OnMatchEnded()
        {
            
            for (int index = ((IReadOnlyCollection<Agent>)((MissionBehaviour)this).Mission.Agents).Count - 1; index >= 0; --index)
                ((MissionBehaviour)this).Mission.Agents[index].FadeOut(true, false);
            ((MissionBehaviour)this).Mission.ClearCorpses();
            ((MissionBehaviour)this).Mission.Teams.Clear();
            ((MissionBehaviour)this).Mission.RemoveSpawnedItemsAndMissiles();
            this._match = (TournamentMatch)null;
            this._endTimer = (BasicTimer)null;
            this._cheerTimer = (BasicTimer)null;
            this._isSimulated = false;
            this._winnerAgent = WinnerAgent.notDecidedYet;
            this._firstFighter = null;
            this._secondFighter = null;
            this._firstParticipant = null;
            this._secondParticipant = null;
            this._firstFighterDestinationReached = false;
            this._secondFighterDestinationReached = false;
            this._firstCouchModeActivated = false;
            this._secondCouchModeActivated = false;
            this._distanceDifferenceCountDown = 0;
        }
        public void SkipMatch(TournamentMatch match)
        {
            this._match = match;
            this.PrepareForMatch();
            this.Simulate();
        }

        public override void OnScoreHit(Agent affectedAgent, Agent affectorAgent, int affectorWeaponKind, bool isBlocked, float damage, float movementSpeedDamageModifier, float hitDistance, AgentAttackType attackType, float shotDifficulty, int weaponCurrentUsageIndex, BoneBodyPartType victimHitBodyPart)
        {
            base.OnScoreHit(affectedAgent, affectorAgent, affectorWeaponKind, isBlocked, damage, movementSpeedDamageModifier, hitDistance, attackType, shotDifficulty, weaponCurrentUsageIndex, victimHitBodyPart);
            
            if (affectorAgent == _secondFighter)
            {
               
                if(affectedAgent.IsMount)
                {
                    if( _winnerAgent == WinnerAgent.notDecidedYet)
                    {
                        _winnerAgent = WinnerAgent.firstAgent;
                        InformationManager.AddQuickInformation(new TextObject("{=*}Match is over. " + _secondFighter.Name + " hit opponent's horse.", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");
                        _firstParticipant.ResetScore();
                        _secondParticipant.ResetScore();
                        _firstParticipant.AddScore(3);
                        _winningDecounter = 0;
                        _firstCouchModeActivated = false;
                        _secondCouchModeActivated = false;
                    }
                }
                else
                {
                    if (victimHitBodyPart == BoneBodyPartType.Head || victimHitBodyPart == BoneBodyPartType.Neck) //baş ya da boyun
                    {
                        _secondParticipant.AddScore(3); //Change score
                    }
                    else if (victimHitBodyPart == BoneBodyPartType.Abdomen || victimHitBodyPart == BoneBodyPartType.Chest) //karın ya da göğüs
                    {
                        _secondParticipant.AddScore(1); //Change score
                    }
                    else if (victimHitBodyPart == BoneBodyPartType.BipedalArmLeft || victimHitBodyPart == BoneBodyPartType.BipedalArmRight) //kollar
                    {
                        _secondParticipant.AddScore(1); //Change score
                    }
                    else if (victimHitBodyPart == BoneBodyPartType.BipedalLegs) //ayaklar
                    {
                        _secondParticipant.AddScore(1); //Change score
                    }
                    else if (victimHitBodyPart == BoneBodyPartType.ShoulderLeft || victimHitBodyPart == BoneBodyPartType.ShoulderRight) //omuzlar
                    {
                        _secondParticipant.AddScore(1); //Change score
                    }

                    if (_winnerAgent == WinnerAgent.notDecidedYet && _secondParticipant.Score >= WinningPoint)
                    {
                        _winningDecounter = 0;
                        _winnerAgent = WinnerAgent.secondAgent;
                        _firstParticipant.ResetScore();
                        _secondParticipant.AddScore(1);

                        Console.WriteLine("{=*}Match is over. " + _secondFighter.Name + " won.");
                        
                    }
                }
                
            }
            else if (affectorAgent == _firstFighter)
            {
                if (affectedAgent.IsMount)
                {
                    if (_winnerAgent == WinnerAgent.notDecidedYet)
                    {
                        _winningDecounter = 0;
                        _winnerAgent = WinnerAgent.secondAgent;
                        InformationManager.AddQuickInformation(new TextObject("{=*}Match is over. " + _firstFighter.Name + " hit opponent's horse.", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");
                        _firstParticipant.ResetScore();
                        _secondParticipant.ResetScore();
                        _secondParticipant.AddScore(3);
                    }

                }
                else
                {
                    if (victimHitBodyPart == BoneBodyPartType.Head || victimHitBodyPart == BoneBodyPartType.Neck) //baş ya da boyun
                    {
                        _firstParticipant.AddScore(3); //Change score
                    }
                    else if (victimHitBodyPart == BoneBodyPartType.Abdomen || victimHitBodyPart == BoneBodyPartType.Chest) //karın ya da göğüs
                    {
                        _firstParticipant.AddScore(1); //Change score
                    }
                    else if (victimHitBodyPart == BoneBodyPartType.BipedalArmLeft || victimHitBodyPart == BoneBodyPartType.BipedalArmRight) //kollar
                    {
                        _firstParticipant.AddScore(1); //Change score
                    }
                    else if (victimHitBodyPart == BoneBodyPartType.BipedalLegs) //ayaklar
                    {
                        _firstParticipant.AddScore(1); //Change score
                    }
                    else if (victimHitBodyPart == BoneBodyPartType.ShoulderLeft || victimHitBodyPart == BoneBodyPartType.ShoulderRight) //omuzlar
                    {
                        _firstParticipant.AddScore(1); //Change score
                    }

                    if (_winnerAgent == WinnerAgent.notDecidedYet && _firstParticipant.Score >= WinningPoint)
                    {
                        _winningDecounter = 0;
                        _winnerAgent = WinnerAgent.firstAgent;

                        _firstParticipant.AddScore(1);
                        _secondParticipant.ResetScore();                        

                        Console.WriteLine("{=*}Match is over. " + _firstFighter.Name + " won.");
                    }
                }
            } 
        }
       
        public void Simulate()
        {
            this._isSimulated = false;

            //if (((IReadOnlyCollection<Agent>)((MissionBehaviour)this).Mission.Agents).Count == 0)
            //{
            //    this._aliveParticipants = this._match.Participants.ToList<TournamentParticipant>();
            //    this._aliveTeams = this._match.Teams.ToList<TournamentTeam>();
            //}
            //TournamentParticipant tournamentParticipant = this._aliveParticipants.FirstOrDefault<TournamentParticipant>((Func<TournamentParticipant, bool>)(x => x.Character == CharacterObject.PlayerCharacter));
            
            //if (tournamentParticipant != null)
            //{
            //    TournamentTeam team = tournamentParticipant.Team;
            //    foreach (TournamentParticipant participant in team.Participants)
            //    {
            //        participant.ResetScore();
            //        this._aliveParticipants.Remove(participant);
            //    }
            //    this._aliveTeams.Remove(team);
            //    this.AddScoreToRemainingTeams();
            //}
            //Dictionary<TournamentParticipant, Tuple<float, float>> dictionary = new Dictionary<TournamentParticipant, Tuple<float, float>>();
            //foreach (TournamentParticipant aliveParticipant in this._aliveParticipants)
            //{
            //    float attackPoints;
            //    float defencePoints;
            //    aliveParticipant.Character.GetSimulationAttackPower(out attackPoints, out defencePoints, aliveParticipant.MatchEquipment);
            //    dictionary.Add(aliveParticipant, new Tuple<float, float>(attackPoints, defencePoints));
            //}
            //int index1 = 0;
            //while (this._aliveParticipants.Count > 1 && this._aliveTeams.Count > 1)
            //{
            //    index1 = (index1 + 1) % this._aliveParticipants.Count;
            //    TournamentParticipant aliveParticipant1 = this._aliveParticipants[index1];
            //    int index2;
            //    TournamentParticipant aliveParticipant2;
            //    do
            //    {
            //        index2 = MBRandom.RandomInt(this._aliveParticipants.Count);
            //        aliveParticipant2 = this._aliveParticipants[index2];
            //    }
            //    while (aliveParticipant1 == aliveParticipant2 || aliveParticipant1.Team == aliveParticipant2.Team);
            //    if ((double)(dictionary[aliveParticipant2].Item2 - dictionary[aliveParticipant1].Item1) > 0.0)
            //    {
            //        dictionary[aliveParticipant2] = new Tuple<float, float>(dictionary[aliveParticipant2].Item1, dictionary[aliveParticipant2].Item2 - dictionary[aliveParticipant1].Item1);
            //    }
            //    else
            //    {
            //        dictionary.Remove(aliveParticipant2);
            //        this._aliveParticipants.Remove(aliveParticipant2);
            //        if (this.CheckIfTeamIsDead(aliveParticipant2.Team))
            //        {
            //            this._aliveTeams.Remove(aliveParticipant2.Team);
            //            this.AddScoreToRemainingTeams();
            //        }
            //        if (index2 < index1)
            //            --index1;
            //    }
            //}

            
            this._isSimulated = true;
        }

        private bool CheckIfTeamIsDead(TournamentTeam affectedParticipantTeam)
        {
            bool flag = true;
            foreach (TournamentParticipant aliveParticipant in this._aliveParticipants)
            {
                if (aliveParticipant.Team == affectedParticipantTeam)
                {
                    flag = false;
                    break;
                }
            }
            return flag;
        }

        public void AddScoreToRemainingTeams()
        {
            foreach (TournamentTeam aliveTeam in this._aliveTeams)
            {
                foreach (TournamentParticipant participant in aliveTeam.Participants)
                participant.AddScore(1);
            }
        }

        public void PrepareForMatch()
        {
            _duelState = DuelState.firstCountdownState;
            _countdownValue = 0;
            _distanceDifferenceCountDown = 0;
            foreach (TournamentTeam team in this._match.Teams)
            {
                
                foreach (TournamentParticipant participant in team.Participants)
                {
                    EquipmentElement horse = new EquipmentElement(Game.Current.ObjectManager.GetObject<ItemObject>("aserai_horse_tournament"));
                    participant.MatchEquipment = new Equipment();
                    participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(EquipmentIndex.Weapon0, new EquipmentElement(MBObjectManager.Instance.GetObject<ItemObject>("vlandia_lance_1_t3"))); //vlandia_sword_1_t2
                    participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(EquipmentIndex.Horse, horse);
                    this.AddArmor(participant);
                }
            }
        }

        private void AddArmor(TournamentParticipant participant)
        {
            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(EquipmentIndex.Head, new EquipmentElement(MBObjectManager.Instance.GetObject<ItemObject>("desert_helmet_with_mail")));
            participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(EquipmentIndex.Body, new EquipmentElement(MBObjectManager.Instance.GetObject<ItemObject>("ringed_desert_armor")));
            //participant.MatchEquipment.AddEquipmentToSlotWithoutAgent(EquipmentIndex.Leg,  new EquipmentElement(MBObjectManager.Instance.GetObject<ItemObject>("rough_tied_bracers")));
        }
        
        public void StartMatch(TournamentMatch match, bool isLastRound)
        {
            _winnerAgent = WinnerAgent.notDecidedYet;
            this._match = match;
            this._isLastRound = isLastRound;
            this.PrepareForMatch();
            ((MissionBehaviour)this).Mission.SetMissionMode(MissionMode.Battle, true);

            List<Team> teamList = new List<Team>();

            int num = 0;
            
            foreach (TournamentTeam team1 in this._match.Teams)
            {
                Team team2 = ((MissionBehaviour)this).Mission.Teams.Add(BattleSideEnum.None, team1.TeamColor, uint.MaxValue, team1.TeamBanner, true, false, true);

                GameEntity spawnPoint = this._spawnPoints[num];
               
                foreach (TournamentParticipant participant in team1.Participants)
                {
                    if (participant.Character.IsPlayerCharacter)
                    {
                        this.SpawnTournamentParticipant(spawnPoint, participant, team2, num);
                        break;
                    }
                }
                foreach (TournamentParticipant participant in team1.Participants)
                {
                    if (!participant.Character.IsPlayerCharacter)
                        this.SpawnTournamentParticipant(spawnPoint, participant, team2, num);
                }
                
                ++num;
                teamList.Add(team2);

                for (int index1 = 0; index1 < teamList.Count; ++index1)
                {
                    for (int index2 = index1 + 1; index2 < teamList.Count; ++index2)
                    {
                        
                        teamList[index1].SetIsEnemyOf(teamList[index2], true);
                    }
                        
                }
                this._aliveParticipants = this._match.Participants.ToList<TournamentParticipant>();
                this._aliveTeams = this._match.Teams.ToList<TournamentTeam>();
            }

        }

        

        private void SpawnTournamentParticipant(GameEntity spawnPoint, TournamentParticipant participant, Team team, int num)
        {
            MatrixFrame globalFrame = spawnPoint.GetGlobalFrame();
            globalFrame.rotation.OrthonormalizeAccordingToForwardAndKeepUpAsZAxis();
            this.SpawnAgentWithJoustingItems(participant, team, globalFrame, num);
        }

        private void SpawnAgentWithJoustingItems(TournamentParticipant participant, Team team, MatrixFrame frame, int num)
        {
            
            CharacterObject character = participant.Character;
            Agent agent = this.Mission.SpawnAgent(new AgentBuildData((IAgentOriginBase)new SimpleAgentOrigin((BasicCharacterObject)character, -1, (Banner)null, participant.Descriptor)).Team(team).InitialFrame(frame).Equipment(participant.MatchEquipment).ClothingColor1(team.Color).Banner(team.Banner).Controller(character.IsPlayerCharacter ? Agent.ControllerType.Player : Agent.ControllerType.AI), false, 0);
            
            if (character.IsPlayerCharacter)
            {
                agent.Health = (float)character.HeroObject.HitPoints;
                this.Mission.PlayerTeam = team;
            }
            else
            {
                agent.AddController(typeof(JoustingTournamentMissionController));
                agent.SetWatchState(AgentAIStateFlagComponent.WatchState.Alarmed);
            }
            agent.WieldInitialWeapons();

            if(num == 0)
            {
                Console.WriteLine("Setting first agent");
                _firstFighter = agent;
                _firstParticipant = participant;

                if(_firstFighter.IsMainAgent)
                {
                    Console.WriteLine("Main agent changing to AI");
                    
                    _firstFighter.Controller = Agent.ControllerType.AI;
                }

                WorldPosition firstTargetPosition = new WorldPosition(this.Mission.Scene, _firstFighter.GetEyeGlobalPosition());
                _firstFighter.SetScriptedPosition(ref firstTargetPosition, true);
            }
            else if(num == 1)
            {
                Console.WriteLine("Setting second agent");
                _secondFighter = agent;
                _secondParticipant = participant;

                if (_secondFighter.IsMainAgent)
                {
                    Console.WriteLine("Main agent changing to AI");
                    _secondFighter.Controller = Agent.ControllerType.AI;
                }

                WorldPosition secondTargetPosition = new WorldPosition(this.Mission.Scene, _secondFighter.GetEyeGlobalPosition());
                _secondFighter.SetScriptedPosition(ref secondTargetPosition, true);
            }
            
            
        }
        
        private void changeDuelState()
        {
            

            switch (_duelState)
            {

                case DuelState.firstCountdownState:

                    Console.WriteLine("Changing to firststate");
                    _duelState = DuelState.firstState;
                    break;

                case DuelState.firstGettingReadyState:

                    Console.WriteLine("Changing to 1stcdstate");
                    _duelState = DuelState.firstCountdownState;
                    break;

                case DuelState.firstState:

                    Console.WriteLine("Changing to 2ndgettingreadystate");
                    _duelState = DuelState.secondGettingReadyState;
                    break;

                case DuelState.secondCountdownState:

                    Console.WriteLine("Changing to 2ndstate");
                    _duelState = DuelState.secondState;
                    break;

                case DuelState.secondGettingReadyState:

                    Console.WriteLine("Changing to 2ndcdstate");
                    _duelState = DuelState.secondCountdownState;
                    break;

                case DuelState.secondState:

                    Console.WriteLine("Changing to 1stGettingReadyState");
                    _duelState = DuelState.firstGettingReadyState;
                    break;

                default:

                    Console.WriteLine("Duel state error occured.");
                    break;

            }
        }

        public bool IsAgentInVolumeBox(VolumeBox volumeBox, Agent agent)
        {

            var globalFrame = volumeBox.GameEntity.GetGlobalFrame();
            MBDebug.RenderDebugFrame(globalFrame, 2);
            var agents = Mission.Current.GetAgentsInRange(globalFrame.origin.AsVec2, 2, true);
            foreach (Agent agentInside in agents)
            {
                if (agentInside == agent)
                {
                    return true;
                }
            }

            return false;
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            _winningDecounter += dt;
            _distanceDifferenceCountDown += dt;
            if(_firstFighter != null && _secondFighter != null)
            {
                if (_duelState == DuelState.firstCountdownState )
                { 
                    _countdownValue += dt;

                    if (_countdownValue >= 0 && _countdownValue < 1 && _countdownTurn == 3)
                    {
                        InformationManager.AddQuickInformation(new TextObject("{=?}Get Ready: 3", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");
                        _countdownTurn--;
                    }
                    else if (_countdownValue >= 1 && _countdownValue < 2 && _countdownTurn == 2)
                    {
                        InformationManager.AddQuickInformation(new TextObject("{=?}Get Ready: 2", (Dictionary<string, TextObject>)null), 1, (BasicCharacterObject)null, "");
                        _countdownTurn--;
                    }
                    else if (_countdownValue >= 2 && _countdownValue < 3 && _countdownTurn == 1)
                    {
                        InformationManager.AddQuickInformation(new TextObject("{=?}Get Ready: 1", (Dictionary<string, TextObject>)null), 2, (BasicCharacterObject)null, "");
                        _countdownTurn--;
                    }
                    else if (_countdownValue >= 3 && _countdownTurn == 0)
                    {
                        InformationManager.AddQuickInformation(new TextObject("{=?}Start! Reach to the other end.", (Dictionary<string, TextObject>)null), 3, (BasicCharacterObject)null, "");
                        _countdownTurn--;
                    }
                    else if (_countdownValue >= 4 && _countdownValue < 5 && _countdownTurn == -1)
                    {
                        
                        _countdownTurn--;

                        _firstFighter.EventControlFlags &= ~Agent.EventControlFlag.DoubleTapToDirectionMask;
                        _firstFighter.EventControlFlags |= Agent.EventControlFlag.DoubleTapToDirectionUp;
                        WorldPosition firstTargetPosition = new WorldPosition(this.Mission.Scene, _wayPoints[1].GlobalPosition);
                        _firstFighter.SetScriptedPosition(ref firstTargetPosition, true);
                        
                        _secondFighter.EventControlFlags &= ~Agent.EventControlFlag.DoubleTapToDirectionMask;
                        _secondFighter.EventControlFlags |= Agent.EventControlFlag.DoubleTapToDirectionUp;
                        WorldPosition secondTargetPosition = new WorldPosition(this.Mission.Scene, _wayPoints[0].GlobalPosition);
                        _secondFighter.SetScriptedPosition(ref secondTargetPosition, true);
                        

                        _countdownTurn = 3;
                        _countdownValue = 0;

                        if (_firstFighter.IsMainAgent)
                        {
                            _firstFighter.Controller = Agent.ControllerType.Player;
                            Console.WriteLine("Main agent controller changed to Player");
                        }

                        if (_secondFighter.IsMainAgent)
                        {
                            _secondFighter.Controller = Agent.ControllerType.Player;
                            Console.WriteLine("Main agent controller changed to Player");
                        }

                        Vec3 firstTarget = _wayPoints[1].GlobalPosition;
                        Vec3 firstStartPoint = _spawnPoints[0].GlobalPosition;

                        Vec3 secondTarget = _wayPoints[0].GlobalPosition;
                        Vec3 secondStartPoint = _spawnPoints[1].GlobalPosition;

                        _prevFirstFighterDistanceDifference = _firstFighter.GetPathDistanceToPoint(ref firstTarget) - _firstFighter.GetPathDistanceToPoint(ref firstStartPoint);
                        _prevSecondFighterDistanceDifference = _secondFighter.GetPathDistanceToPoint(ref secondTarget) - _secondFighter.GetPathDistanceToPoint(ref secondStartPoint);
                        _distanceDifferenceCountDown = 0;
                        changeDuelState();
                    }
                }
                else if (_duelState == DuelState.secondCountdownState)
                {
                    
                    _countdownValue += dt;

                    if (_countdownValue >= 0 && _countdownValue < 1 && _countdownTurn == 3)
                    {
                        InformationManager.AddQuickInformation(new TextObject("{=?}Get Ready: 3", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");
                        _countdownTurn--;
                    }
                    else if (_countdownValue >= 1 && _countdownValue < 2 && _countdownTurn == 2)
                    {
                        InformationManager.AddQuickInformation(new TextObject("{=?}Get Ready: 2", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");
                        _countdownTurn--;
                    }
                    else if (_countdownValue >= 2 && _countdownValue < 3 && _countdownTurn == 1)
                    {
                        InformationManager.AddQuickInformation(new TextObject("{=?}Get Ready: 1", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");
                        _countdownTurn--;
                    }
                    else if (_countdownValue >= 3 && _countdownValue < 4 && _countdownTurn == 0)
                    {
                        InformationManager.AddQuickInformation(new TextObject("{=?}Start! Reach to the other end.", (Dictionary<string, TextObject>)null), 1, (BasicCharacterObject)null, "");
                        _countdownTurn--;
                    }
                    else if(_countdownValue >= 4 && _countdownValue < 5 && _countdownTurn == -1)
                    {
                        _countdownTurn--;
                        
                        _firstFighter.EventControlFlags &= ~Agent.EventControlFlag.DoubleTapToDirectionMask;
                        _firstFighter.EventControlFlags |= Agent.EventControlFlag.DoubleTapToDirectionUp;
                        WorldPosition firstTargetPosition = new WorldPosition(this.Mission.Scene, _wayPoints[0].GlobalPosition);
                        _firstFighter.SetScriptedPosition(ref firstTargetPosition, true);


                        _secondFighter.EventControlFlags &= ~Agent.EventControlFlag.DoubleTapToDirectionMask;
                        _secondFighter.EventControlFlags |= Agent.EventControlFlag.DoubleTapToDirectionUp;
                        WorldPosition secondTargetPosition = new WorldPosition(this.Mission.Scene, _wayPoints[1].GlobalPosition);
                        _secondFighter.SetScriptedPosition(ref secondTargetPosition, true);

                        _countdownTurn = 3;
                        _countdownValue = 0;

                        if (_firstFighter.IsMainAgent)
                        {
                            _firstFighter.Controller = Agent.ControllerType.Player;
                            Console.WriteLine("Main agent controller changed to Player");
                        }

                        if (_secondFighter.IsMainAgent)
                        {
                            _secondFighter.Controller = Agent.ControllerType.Player;
                            Console.WriteLine("Main agent controller changed to Player");
                        }

                        Vec3 firstTarget = _wayPoints[0].GlobalPosition;
                        Vec3 firstStartPoint = _spawnPoints[1].GlobalPosition;

                        Vec3 secondTarget = _wayPoints[1].GlobalPosition;
                        Vec3 secondStartPoint = _spawnPoints[0].GlobalPosition;

                        _prevFirstFighterDistanceDifference = _firstFighter.GetPathDistanceToPoint(ref firstTarget) - _firstFighter.GetPathDistanceToPoint(ref firstStartPoint);
                        _prevSecondFighterDistanceDifference = _secondFighter.GetPathDistanceToPoint(ref secondTarget) - _secondFighter.GetPathDistanceToPoint(ref secondStartPoint);
                        _distanceDifferenceCountDown = 0;
                        changeDuelState();
                    }
                }
                else if (_duelState == DuelState.firstState)
                {
                    
                    if(_distanceDifferenceCountDown >= 0.5f)
                    {
                        Vec3 firstTarget = _wayPoints[1].GlobalPosition;
                        Vec3 firstStartPoint = _spawnPoints[0].GlobalPosition;

                        Vec3 secondTarget = _wayPoints[0].GlobalPosition;
                        Vec3 secondStartPoint = _spawnPoints[1].GlobalPosition;

                        float newFirstFighterDistanceDifference = _firstFighter.GetPathDistanceToPoint(ref firstTarget) - _firstFighter.GetPathDistanceToPoint(ref firstStartPoint);
                        float newSecondFighterDistanceDifference = _secondFighter.GetPathDistanceToPoint(ref secondTarget) - _secondFighter.GetPathDistanceToPoint(ref secondStartPoint);

                        if (newFirstFighterDistanceDifference > _prevFirstFighterDistanceDifference)
                        {
                            if (_winnerAgent == WinnerAgent.notDecidedYet)
                            {
                                InformationManager.AddQuickInformation(new TextObject("{=*}Match is over. " + _firstFighter.Name + " did not move forward.", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");

                                _winningDecounter = 0;
                                _winnerAgent = WinnerAgent.secondAgent;

                                _firstParticipant.ResetScore();
                                _secondParticipant.ResetScore();

                                _secondParticipant.AddScore(3);
                            }

                        }
                        else
                        {
                            _prevFirstFighterDistanceDifference = newFirstFighterDistanceDifference;
                        }

                        if (newSecondFighterDistanceDifference > _prevSecondFighterDistanceDifference)
                        {
                            if (_winnerAgent == WinnerAgent.notDecidedYet)
                            {
                                InformationManager.AddQuickInformation(new TextObject("{=*}Match is over. " + _secondFighter.Name + " did not move forward.", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");

                                _winningDecounter = 0;
                                _winnerAgent = WinnerAgent.firstAgent;

                                _firstParticipant.ResetScore();
                                _secondParticipant.ResetScore();

                                _firstParticipant.AddScore(3);
                            }
                        }
                        else
                        {
                            _prevSecondFighterDistanceDifference = newSecondFighterDistanceDifference;
                        }

                        _distanceDifferenceCountDown = 0;
                    }
                    
                    //Below code doesnt work...
                    //------------------------------------------------------------------------

                    if (IsWeaponCouchable(_firstFighter.WieldedWeapon) && _firstFighter.IsPassiveUsageConditionsAreMet /* && _firstFighter.GetCurrentVelocity().Length > 9.2f && IsAgentInVolumeBox(_volumeBoxes[0].GetScriptComponents<VolumeBox>().First() , _firstFighter)*/ && !_firstCouchModeActivated)
                    {
                        //Console.WriteLine("First volumebox reached by first agent");
                        Console.WriteLine("First couch mode entered");
                        _firstCouchModeActivated = true;

                        _firstFighter.DisableScriptedMovement();

                        _firstFighter.EventControlFlags |= Agent.EventControlFlag.ToggleAlternativeWeapon;

                        WorldPosition firstTargetPosition = new WorldPosition(this.Mission.Scene, _wayPoints[1].GlobalPosition);
                        _firstFighter.SetScriptedPosition(ref firstTargetPosition, true);
                    }


                    if (IsWeaponCouchable(_secondFighter.WieldedWeapon) && _secondFighter.IsPassiveUsageConditionsAreMet /*_secondFighter.GetCurrentVelocity().Length > 9 && IsAgentInVolumeBox(_volumeBoxes[1].GetScriptComponents<VolumeBox>().First(), _secondFighter)*/ && !_secondCouchModeActivated)
                    {
                        //Console.WriteLine("Second volumebox reached by second agent");
                        Console.WriteLine("Second couch mode entered");
                        _secondCouchModeActivated = true;

                        _secondFighter.DisableScriptedMovement();

                        _secondFighter.EventControlFlags |= Agent.EventControlFlag.ToggleAlternativeWeapon;

                        WorldPosition firstTargetPosition = new WorldPosition(this.Mission.Scene, _wayPoints[0].GlobalPosition);
                        _secondFighter.SetScriptedPosition(ref firstTargetPosition, false);
                    }

                    //------------------------------------------------------------------------

                    if (Math.Abs(_firstFighter.GetEyeGlobalPosition().X - _wayPoints[1].GlobalPosition.X) < 1 && Math.Abs(_firstFighter.GetEyeGlobalPosition().Y - _wayPoints[1].GlobalPosition.Y) < 1)
                    {
                        _firstFighterDestinationReached = true;

                        if (_firstFighter.IsMainAgent)
                        {
                            _firstFighter.Controller = Agent.ControllerType.AI;
                            Console.WriteLine("Main agent controller changed to AI");
                        }
                    }
                    

                    if(Math.Abs(_secondFighter.GetEyeGlobalPosition().X - _wayPoints[0].GlobalPosition.X) < 1 && Math.Abs(_secondFighter.GetEyeGlobalPosition().Y - _wayPoints[0].GlobalPosition.Y) < 1)
                    {
                        _secondFighterDestinationReached = true;
                        if (_secondFighter.IsMainAgent)
                        {
                            _secondFighter.Controller = Agent.ControllerType.AI;
                            Console.WriteLine("Main agent controller changed to AI");
                        }
                    }

                    if ( _firstFighterDestinationReached  && _secondFighterDestinationReached )
                    {
                        Console.WriteLine("first state positions are set.");

                        WorldPosition firstTargetPosition = new WorldPosition(this.Mission.Scene, _spawnPoints[1].GlobalPosition);
                        _firstFighter.SetScriptedPositionAndDirection(ref firstTargetPosition, 90f * MBMath.DegreesToRadians, true, Agent.AIScriptedFrameFlags.DoNotRun);

                        WorldPosition secondTargetPosition = new WorldPosition(this.Mission.Scene, _spawnPoints[0].GlobalPosition);
                        _secondFighter.SetScriptedPositionAndDirection(ref secondTargetPosition, 270f * MBMath.DegreesToRadians, true, Agent.AIScriptedFrameFlags.DoNotRun);

                        _firstCouchModeActivated = false;
                        _secondCouchModeActivated = false;

                        _firstFighterDestinationReached = false;
                        _secondFighterDestinationReached = false;
                        changeDuelState();
                    }

                    if(Math.Abs(_firstFighter.GetEyeGlobalPosition().Y - _spawnPoints[0].GlobalPosition.Y) > MaxDistanceDifference && !(_firstFighterDestinationReached))
                    {
                        if (_winnerAgent == WinnerAgent.notDecidedYet)
                        {
                            InformationManager.AddQuickInformation(new TextObject("{=*}Match is over. " + _firstFighter.Name + " got far from the fence.", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");

                            _winningDecounter = 0;
                            _winnerAgent = WinnerAgent.secondAgent;

                            _firstParticipant.ResetScore();
                            _secondParticipant.ResetScore();

                            _secondParticipant.AddScore(3);
                            
                        }
                    }

                    if (Math.Abs(_secondFighter.GetEyeGlobalPosition().Y - _spawnPoints[1].GlobalPosition.Y) > MaxDistanceDifference && !(_secondFighterDestinationReached))
                    {
                        if (_winnerAgent == WinnerAgent.notDecidedYet)
                        {
                            InformationManager.AddQuickInformation(new TextObject("{=*}Match is over. " + _secondFighter.Name + " got far from the fence.", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");

                            _winningDecounter = 0;
                            _winnerAgent = WinnerAgent.firstAgent;

                            _firstParticipant.ResetScore();
                            _secondParticipant.ResetScore();

                            _firstParticipant.AddScore(3);

                        }
                    }


                }
                else if (_duelState == DuelState.secondGettingReadyState)
                {
                    
                    if (Math.Abs(_firstFighter.GetEyeGlobalPosition().X - _spawnPoints[1].GlobalPosition.X) < 1 && Math.Abs(_firstFighter.GetEyeGlobalPosition().Y - _spawnPoints[1].GlobalPosition.Y) < 1 && Math.Abs(_secondFighter.GetEyeGlobalPosition().X - _spawnPoints[0].GlobalPosition.X) < 1 && Math.Abs(_secondFighter.GetEyeGlobalPosition().Y - _spawnPoints[0].GlobalPosition.Y) < 1)
                    {
                        changeDuelState();
                    }
                }
                else if (_duelState == DuelState.secondState)
                {
                    //Below code doesnt work...
                    //------------------------------------------------------------------------

                    if (IsWeaponCouchable(_firstFighter.WieldedWeapon) && _firstFighter.IsPassiveUsageConditionsAreMet /* && _firstFighter.GetCurrentVelocity().Length > 9.2f && IsAgentInVolumeBox(_volumeBoxes[0].GetScriptComponents<VolumeBox>().First() , _firstFighter) && !_firstCouchModeActivated*/)
                    {
                        //Console.WriteLine("First volumebox reached by first agent");
                        Console.WriteLine("First couch mode entered");
                        _firstCouchModeActivated = true;
                        _firstFighter.EventControlFlags |= Agent.EventControlFlag.ToggleAlternativeWeapon;
                    }


                    if (IsWeaponCouchable(_secondFighter.WieldedWeapon) && _secondFighter.IsPassiveUsageConditionsAreMet /*_secondFighter.GetCurrentVelocity().Length > 9 && IsAgentInVolumeBox(_volumeBoxes[1].GetScriptComponents<VolumeBox>().First(), _secondFighter) && !_secondCouchModeActivated*/)
                    {
                        //Console.WriteLine("Second volumebox reached by second agent");
                        Console.WriteLine("Second couch mode entered");
                        _secondCouchModeActivated = true;
                        _secondFighter.EventControlFlags |= Agent.EventControlFlag.ToggleAlternativeWeapon;
                    }

                    //------------------------------------------------------------------------

                    if (_distanceDifferenceCountDown >= 0.5f)
                    {
                        Vec3 firstTarget = _wayPoints[0].GlobalPosition;
                        Vec3 firstStartPoint = _spawnPoints[1].GlobalPosition;

                        Vec3 secondTarget = _wayPoints[1].GlobalPosition;
                        Vec3 secondStartPoint = _spawnPoints[0].GlobalPosition;

                        float newFirstFighterDistanceDifference = _firstFighter.GetPathDistanceToPoint(ref firstTarget) - _firstFighter.GetPathDistanceToPoint(ref firstStartPoint);
                        float newSecondFighterDistanceDifference = _secondFighter.GetPathDistanceToPoint(ref secondTarget) - _secondFighter.GetPathDistanceToPoint(ref secondStartPoint);

                        if (newFirstFighterDistanceDifference > _prevFirstFighterDistanceDifference)
                        {
                            if (_winnerAgent == WinnerAgent.notDecidedYet)
                            {
                                InformationManager.AddQuickInformation(new TextObject("{=*}Match is over. " + _firstFighter.Name + " did not move forward.", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");

                                _winningDecounter = 0;
                                _winnerAgent = WinnerAgent.secondAgent;

                                _firstParticipant.ResetScore();
                                _secondParticipant.ResetScore();

                                _secondParticipant.AddScore(3);
                            }

                        }
                        else
                        {
                            _prevFirstFighterDistanceDifference = newFirstFighterDistanceDifference;
                        }

                        if (newSecondFighterDistanceDifference > _prevSecondFighterDistanceDifference)
                        {
                            if (_winnerAgent == WinnerAgent.notDecidedYet)
                            {
                                InformationManager.AddQuickInformation(new TextObject("{=*}Match is over. " + _secondFighter.Name + " did not move forward.", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");

                                _winningDecounter = 0;
                                _winnerAgent = WinnerAgent.firstAgent;

                                _firstParticipant.ResetScore();
                                _secondParticipant.ResetScore();

                                _firstParticipant.AddScore(3);
                            }
                        }
                        else
                        {
                            _prevSecondFighterDistanceDifference = newSecondFighterDistanceDifference;
                        }

                        _distanceDifferenceCountDown = 0;
                    }

                    if (Math.Abs(_firstFighter.GetEyeGlobalPosition().X - _wayPoints[0].GlobalPosition.X) < 1 && Math.Abs(_firstFighter.GetEyeGlobalPosition().Y - _wayPoints[0].GlobalPosition.Y) < 1)
                    {
                        _firstFighterDestinationReached = true;

                        if (_firstFighter.IsMainAgent)
                        {
                            _firstFighter.Controller = Agent.ControllerType.AI;
                            Console.WriteLine("Main agent controller changed to AI");
                        }
                    }

                    if(Math.Abs(_secondFighter.GetEyeGlobalPosition().X - _wayPoints[1].GlobalPosition.X) < 1 && Math.Abs(_secondFighter.GetEyeGlobalPosition().Y - _wayPoints[1].GlobalPosition.Y) < 1)
                    {
                        _secondFighterDestinationReached = true;

                        if (_secondFighter.IsMainAgent)
                        {
                            _secondFighter.Controller = Agent.ControllerType.AI;
                            Console.WriteLine("Main agent controller changed to AI");
                        }
                    }

                    if (_firstFighterDestinationReached && _secondFighterDestinationReached)
                    {
                        Console.WriteLine("Second-getting-ready positions are set.");
                        WorldPosition firstTargetPosition = new WorldPosition(this.Mission.Scene, _spawnPoints[0].GlobalPosition);
                        _firstFighter.SetScriptedPositionAndDirection(ref firstTargetPosition, 270f * MBMath.DegreesToRadians, true, Agent.AIScriptedFrameFlags.DoNotRun);
                        

                        WorldPosition secondTargetPosition = new WorldPosition(this.Mission.Scene, _spawnPoints[1].GlobalPosition);
                        _secondFighter.SetScriptedPositionAndDirection(ref secondTargetPosition, 90f * MBMath.DegreesToRadians, true, Agent.AIScriptedFrameFlags.DoNotRun);

                        _firstCouchModeActivated = false;
                        _secondCouchModeActivated = false;
                        _firstFighterDestinationReached = false;
                        _secondFighterDestinationReached = false;
                        changeDuelState();
                    }

                    if (Math.Abs(_firstFighter.GetEyeGlobalPosition().Y - _spawnPoints[1].GlobalPosition.Y) > MaxDistanceDifference)
                    {
                        if (_winnerAgent == WinnerAgent.notDecidedYet)
                        {
                            InformationManager.AddQuickInformation(new TextObject("{=*}Match is over. " + _firstFighter.Name + " got far from the fence.", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");

                            _winningDecounter = 0;
                            _winnerAgent = WinnerAgent.secondAgent;

                            _firstParticipant.ResetScore();
                            _secondParticipant.ResetScore();

                            _secondParticipant.AddScore(3);

                        }
                    }

                    if (Math.Abs(_secondFighter.GetEyeGlobalPosition().Y - _spawnPoints[0].GlobalPosition.Y) > MaxDistanceDifference)
                    {
                        if (_winnerAgent == WinnerAgent.notDecidedYet)
                        {
                            InformationManager.AddQuickInformation(new TextObject("{=*}Match is over. " + _secondFighter.Name + " got far from the fence.", (Dictionary<string, TextObject>)null), 0, (BasicCharacterObject)null, "");

                            _winningDecounter = 0;
                            _winnerAgent = WinnerAgent.firstAgent;

                            _firstParticipant.ResetScore();
                            _secondParticipant.ResetScore();

                            _firstParticipant.AddScore(3);

                        }
                    }
                }
                else if (_duelState == DuelState.firstGettingReadyState)
                {
                    
                    if (Math.Abs(_firstFighter.GetEyeGlobalPosition().X - _spawnPoints[0].GlobalPosition.X) < 1 && Math.Abs(_firstFighter.GetEyeGlobalPosition().Y - _spawnPoints[0].GlobalPosition.Y) < 1 && Math.Abs(_secondFighter.GetEyeGlobalPosition().X - _spawnPoints[1].GlobalPosition.X) < 1 && Math.Abs(_secondFighter.GetEyeGlobalPosition().Y - _spawnPoints[1].GlobalPosition.Y) < 1)
                    {
                        changeDuelState();
                    }
                }
            }
            
            
        }

        
        private bool IsWeaponCouchable(MissionWeapon weapon)
        {
            if (weapon.IsEmpty)
            {
                return false;
            }
            foreach (var item in weapon.PrimaryItem.Weapons)
            {
                if (MBItem.GetItemIsPassiveUsage(item.ItemUsage))
                {
                    return true;
                }
            }
            return false;
        }

        private void SetSceneObjects()
        {
            
            _spawnPoints = new List<GameEntity>();
            _wayPoints = new List<GameEntity>();
            _volumeBoxes = new List<GameEntity>();
            
            GameEntity spawnPointTag0 = ((MissionBehaviour)this).Mission.Scene.FindEntityWithTag("jousting_spawn_0");
            GameEntity spawnPointTag1 = ((MissionBehaviour)this).Mission.Scene.FindEntityWithTag("jousting_spawn_1");

            if (spawnPointTag0 != null && spawnPointTag1 != null)
            {
                this._spawnPoints.Add(spawnPointTag0);
                this._spawnPoints.Add(spawnPointTag1);

            }

            GameEntity wayPointTag0 = ((MissionBehaviour)this).Mission.Scene.FindEntityWithTag("jousting_waypoint_0");
            GameEntity wayPointTag1 = ((MissionBehaviour)this).Mission.Scene.FindEntityWithTag("jousting_waypoint_1");

            if (wayPointTag0 != null && wayPointTag1 != null)
            {
                this._wayPoints.Add(wayPointTag0);
                this._wayPoints.Add(wayPointTag1);
            }
            _volumeBoxes = Mission.Current.GetActiveEntitiesWithScriptComponentOfType<VolumeBox>().ToList<GameEntity>();

            Console.WriteLine("spawnpoints: {0}, waypoints: {1}, volumeboxes: {2}.", _spawnPoints.Count(), _wayPoints.Count(), _volumeBoxes.Count());
           

            Console.WriteLine("spawn point 0: X:{0}, Y:{1}, Z:{2}", _spawnPoints[0].GlobalPosition.X, _spawnPoints[0].GlobalPosition.Y, _spawnPoints[0].GlobalPosition.Z);
            Console.WriteLine("spawn point 1: X:{0}, Y:{1}, Z:{2}", _spawnPoints[1].GlobalPosition.X, _spawnPoints[1].GlobalPosition.Y, _spawnPoints[1].GlobalPosition.Z);
            Console.WriteLine("wayPoint 0: X:{0}, Y:{1}, Z:{2}", _wayPoints[0].GlobalPosition.X, _wayPoints[0].GlobalPosition.Y, _wayPoints[0].GlobalPosition.Z);
            Console.WriteLine("wayPoint 1: X:{0}, Y:{1}, Z:{2}", _wayPoints[1].GlobalPosition.X, _wayPoints[1].GlobalPosition.Y, _wayPoints[1].GlobalPosition.Z);
        }
        public override void AfterStart()
        {
            
            TournamentBehavior.DeleteTournamentSetsExcept(((MissionBehaviour)this).Mission.Scene.FindEntityWithTag("tournament_fight"));
            
            SetSceneObjects();

            //GameEntity spawnEntity1 = GameEntity.CreateEmpty(this.Mission.Scene);
            //GameEntity spawnEntity2 = GameEntity.CreateEmpty(this.Mission.Scene);

            //spawnEntity1.SetLocalPosition(new Vec3(372, 436.7f, 2)); //437
            //spawnEntity2.SetLocalPosition(new Vec3(412, 439.3f, 2));

            //var frame1 = spawnEntity1.GetGlobalFrame();
            //frame1.Rotate(3f * (float)Math.PI / 2f, new Vec3(0, 0, 1));
            //spawnEntity1.SetFrame(ref frame1);

            //var frame2 = spawnEntity2.GetGlobalFrame();
            //frame2.Rotate((float)Math.PI / 2f, new Vec3(0, 0, 1));
            //spawnEntity2.SetFrame(ref frame2);

            //this._spawnPoints.Add(spawnEntity1);
            //this._spawnPoints.Add(spawnEntity2);

            if (this._spawnPoints.Count < 2)
            {
                Console.WriteLine("@@@@@@@@@@@@ Warning: Spawn point count less than 2.");
                //this._spawnPoints = ((MissionBehaviour)this).Mission.Scene.FindEntitiesWithTag("sp_arena").ToList<GameEntity>();
            }
                
        }
        
        
    }

    [ViewCreatorModule]
    public class TournamentMissionViews
    {
        [ViewMethod("TournamentJousting2")]
        public static MissionView[] OpenTournamentJoustingMission(Mission mission)
        {
            List<MissionView> missionViews = new List<MissionView>();
            missionViews.Add(new CampaignMissionView());
            missionViews.Add(new ConversationCameraView());
            missionViews.Add(ViewCreator.CreateMissionSingleplayerEscapeMenu());
            missionViews.Add(ViewCreator.CreateOptionsUIHandler());
            missionViews.Add(SandBoxViewCreator.CreateMissionTournamentView());
            missionViews.Add(new MissionAudienceHandler(0.4f + MBRandom.RandomFloat * 0.6f));
            missionViews.Add(ViewCreator.CreateMissionAgentStatusUIHandler(mission));
            missionViews.Add(ViewCreator.CreateMissionMainAgentEquipmentController(mission));
            //missionViews.Add(ViewCreator.CreateMissionMainAgentCheerControllerView(mission));
            missionViews.Add(new MusicTournamentMissionView());
            missionViews.Add(new MissionSingleplayerUIHandler());
            missionViews.Add(ViewCreator.CreateSingleplayerMissionKillNotificationUIHandler());
            //missionViews.Add(new MissionArenaMusicView());
            missionViews.Add(new MusicMissionView(new MusicMissionTournamentComponent()));
            missionViews.Add(ViewCreator.CreateMissionAgentLabelUIHandler(mission));
            missionViews.Add(new MissionItemContourControllerView());
            return missionViews.ToArray();
        }
    }


    public class JoustingTournamentTypeDefiner : SaveableCampaignBehaviorTypeDefiner
    {
        public JoustingTournamentTypeDefiner() : base(20117)
        {
        }
        protected override void DefineClassTypes()
        {
            AddClassDefinition(typeof(JoustingTournamentGame), 1);
        }
    }

}
