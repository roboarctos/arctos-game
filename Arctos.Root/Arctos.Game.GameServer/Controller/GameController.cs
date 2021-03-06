﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Arctos.Game.Middleware.Logic.Model.Model;
using Arctos.Game.Model;
using ArctosGameServer.Controller.Events;
using ArctosGameServer.Domain;
using ArctosGameServer.Service;
using Game = ArctosGameServer.Domain.Game;

namespace ArctosGameServer.Controller
{
    /// <summary>
    /// The GameController
    /// </summary>
    public class GameController : IObserver<Tuple<Guid, GameEvent>>
    {
        private readonly ConcurrentQueue<Tuple<Guid, GameEvent>> _receivedEvents =
            new ConcurrentQueue<Tuple<Guid, GameEvent>>();

        private Game _game;
        private Dictionary<string, Player> _players = new Dictionary<string, Player>();

        private GameTcpServer _server;

        /// <summary>
        /// Constructor of the Game Controller
        /// </summary>
        /// <param name="server"></param>
        /// <param name="configuration"></param>
        public GameController(GameTcpServer server, GameConfiguration configuration)
        {
            _server = server;

            _game = new Game(configuration);
        }

        /// <summary>
        /// When true the GameController will exit from the loop
        /// </summary>
        public bool ShutdownRequested { get; set; }

        /// <summary>
        /// Game Loop
        /// </summary>
        public void Loop()
        {
            LogLine("Starting server " + GameTcpServer.FindIp());

            while (!ShutdownRequested)
            {
                // Remove all kicked players
                var toRemove = _players.Where(pair => pair.Value.Kicked == true)
                    .Select(pair => pair.Value)
                    .ToList();

                foreach (var p in toRemove)
                {
                    RemovePlayer(p);
                    RemoveGui(p);
                }

                // Process all received events
                ProcessEvents();

                // Check for the game state
                switch (_game.State)
                {
                    case GameState.Waiting:
                        if (PlayersReady())
                        {
                            SetGameReady();
                        }
                        break;
                    case GameState.Ready:
                        if (!PlayersReady())
                        {
                            SetGameWaiting();
                        }
                        break;
                    case GameState.Started:
                        // Check for finished players
                        foreach (var player in _players.Values)
                        {
                            if (player.HasRecentlyFinished())
                            {
                                FinishPlayer(player);
                            }
                        }

                        // Check if all player have finished
                        if (_players.Values.Count > 0 && _players.Values.Count(x => x.FinishedGame == false) == 0)
                        {
                            FinishGame();
                        }
                        break;
                    case GameState.Finished:
                        break;
                    
                }

                // If Game Reset was requested do it
                if (_game.RequestReset)
                {
                    _game.RequestReset = false;
                    Reset();
                }
            }
        }

        /// <summary>
        /// Set the game state to waiting
        /// </summary>
        private void SetGameWaiting()
        {
            LogLine("Game is not ready to start anymore");

            // Game changed from ready to not ready
            _game.State = GameState.Waiting;

            // Notify CUs and GUIs
            _server.Send(new GameEvent(GameEvent.Type.GameReady, null));

            // Send Event
            OnGameReadyEvent(new GameReadeEventArgs() {Ready = false});
        }

        /// <summary>
        /// Set the game state to ready
        /// </summary>
        private void SetGameReady()
        {
            LogLine("Game is ready to start");

            // Game changed to ready
            _game.State = GameState.Ready;

            // Create the path
            var path = _game.Path;
            foreach (var player in _players.Values)
            {
                player.Map.setPath(path);
            }

            // Notify CUs and GUIs
            var sendPath = new Path(path);
            _server.Send(new GameEvent(GameEvent.Type.GameReady, sendPath));

            // Send Event
            OnGameReadyEvent(new GameReadeEventArgs() {Ready = true});
        }

        /// <summary>
        /// Finish the game
        /// </summary>
        private void FinishGame()
        {
            // All players are finished
            LogLine("All players finished the game");

            _game.State = GameState.Finished;

            // Get winner
            var winner = _players.Values.OrderBy(x => x.Duration).First();

            // Only tell the winner if there is more than just one player
            foreach (var player in _players.Values)
            {
                // Send game event
                var gameEvent = new GameEvent(GameEvent.Type.GameFinish, player.Equals(winner));

                if (!player.GuiId.Equals(Guid.Empty))
                {
                    _server.Send(gameEvent, player.GuiId);
                }

                // Send event
                OnGameFinishedEvent(new GameFinishEventArgs() {Finished = true});
            }
        }

        /// <summary>
        /// Finish the player and send his counter
        /// </summary>
        /// <param name="player"></param>
        private void FinishPlayer(Player player)
        {
            player.FinishedGame = true;
            player.EndCounter();

            // Send log
            LogLine("Player " + player.Name + " finished with " + player.Duration);

            // Send GUI
            if (!player.GuiId.Equals(Guid.Empty))
            {
                // Send Counter
                _server.Send(new GameEvent(GameEvent.Type.PlayerFinish, player.Duration.TotalMilliseconds), player.GuiId);
            }

            // Send Event
            OnPlayerFinishedEvent(new PlayerFinishedEventArgs() {Player = player});
        }

        /// <summary>
        /// Kicks the player from the game
        /// </summary>
        /// <param name="player"></param>
        public void KickPlayer(Player player)
        {
            // Notify GUI and Player that they will be kicked
            if (!player.ControlUnitId.Equals(Guid.Empty))
            {
                _server.Send(new GameEvent(GameEvent.Type.PlayerKicked, null), player.ControlUnitId);
            }
            if (!player.GuiId.Equals(Guid.Empty))
            {
                _server.Send(new GameEvent(GameEvent.Type.PlayerKicked, null), player.GuiId);
            }

            // Remove them from the game
            player.Kicked = true;
        }

        /// <summary>
        /// Process All Events
        /// </summary>
        private void ProcessEvents()
        {
            Tuple<Guid, GameEvent> e = null;
            while (_receivedEvents.TryDequeue(out e))
            {
                switch (e.Item2.EventType)
                {
                    case GameEvent.Type.PlayerRequest:
                    {
                        var playerName = (string) e.Item2.Data;
                        PlayerRequest(e.Item1, playerName);
                    }
                        break;
                    case GameEvent.Type.PlayerLeft:
                    {
                        PlayerLeft(e.Item1);
                    }
                        break;
                    case GameEvent.Type.ConnectionLost:
                    {
                        ConnectionLost(e.Item1, (string) e.Item2.Data);
                    }
                        break;
                    case GameEvent.Type.GuiLeft:
                    {
                        GuiLeft(e.Item1);
                    }
                        break;
                    case GameEvent.Type.GuiRequest:
                    {
                        var playerName = (string) e.Item2.Data;
                        GuiRequest(e.Item1, playerName);
                    }
                        break;
                    case GameEvent.Type.GuiJoined:
                        // Should never occur
                        break;
                    case GameEvent.Type.AreaUpdate:
                    {
                        var area = (string) e.Item2.Data;
                        AreaUpdate(e.Item1, area);
                    }
                        break;
                }
            }
        }

        /// <summary>
        /// Will be called when the CU closes the unit
        /// </summary>
        /// <param name="id"></param>
        private void PlayerLeft(Guid id)
        {
            var player = _players.Values.FirstOrDefault(x => x.ControlUnitId.Equals(id));
            if (player != null)
            {
                LogLine("Player " + player.Name + " left");

                // Notify GUI if existing
                if (!player.GuiId.Equals(Guid.Empty))
                {
                    _server.Send(new GameEvent(GameEvent.Type.PlayerLeft, null), player.GuiId);
                }

                PausePlayer(player);
            }
            else
            {
                LogLine("Unknown player left");
            }
        }

        /// <summary>
        /// Will be called when the GUI closes
        /// </summary>
        /// <param name="id"></param>
        private void GuiLeft(Guid id)
        {
            var player = _players.Values.FirstOrDefault(x => x.GuiId.Equals(id));
            if (player != null)
            {
                LogLine("GUI for player " + player.Name + " left");

                // Notify CU
                if (!player.ControlUnitId.Equals(Guid.Empty))
                {
                    _server.Send(new GameEvent(GameEvent.Type.GuiLeft, null), player.ControlUnitId);
                }

                RemoveGui(player);
            }
            else
            {
                LogLine("GUI for unknown player left");
            }
        }

        /// <summary>
        /// Removes the player
        /// </summary>
        /// <param name="player"></param>
        private void RemovePlayer(Player player)
        {
            _players.Remove(player.Name);

            // Add map
            if (player.Map != null)
            {
                _game.AddMap(player.Map);
            }

            if (!player.ControlUnitId.Equals(Guid.Empty))
            {
                try
                {
                    _server.Disconnect(player.ControlUnitId);
                }
                catch (Exception e)
                {
                    LogLine("Error when disconnecting CU: " + e.Message);
                }
            }
           

            // Send event
            OnPlayerLeftEvent(new PlayerLeftEventArgs() {Player = player});
        }

        /// <summary>
        /// Removes the GUI
        /// </summary>
        /// <param name="player"></param>
        private void RemoveGui(Player player)
        {
            if (!player.GuiId.Equals(Guid.Empty))
            {
                try
                {
                    _server.Disconnect(player.GuiId);
                }
                catch (Exception e)
                {
                    LogLine("Error when disconnecting GUI " + e.Message);
                }
            }

            player.GuiId = Guid.Empty;

            // Send event
            OnGuiChangedEvent(new GuiChangedEventArgs(player));
        }

        /// <summary>
        /// Pauses the player, meaning that the player won't be able to update its status. A paused player can continue when reconnected
        /// </summary>
        /// <param name="player"></param>
        private void PausePlayer(Player player)
        {
            LogLine("Pausing " + player.Name);
            player.Pause = true;

            // Send event
            OnPlayerLostEvent(new PlayerLostEventArgs() {Player = player, Lost = true});
        }

        /// <summary>
        /// Let the player resume his game
        /// </summary>
        /// <param name="player"></param>
        private void ResumePlayer(Player player)
        {
            LogLine("Resuming " + player.Name);
            player.Pause = false;

            // Send event
            OnPlayerLostEvent(new PlayerLostEventArgs() {Player = player, Lost = false});
        }

        /// <summary>
        /// Will be called if a connection to either a CU or GUI is closed
        /// </summary>
        /// <param name="id"></param>
        /// <param name="data"></param>
        private void ConnectionLost(Guid id, string data)
        {
            // Check if it was a CU
            var player = _players.Values.FirstOrDefault(x => x.ControlUnitId.Equals(id));
            if (player != null)
            {
                LogLine("Connetion lost of CU for player " + player.Name);
                player.ControlUnitId = Guid.Empty;

                // Notify GUI if existing
                if (!player.GuiId.Equals(Guid.Empty))
                {
                    _server.Send(new GameEvent(GameEvent.Type.PlayerLost, null), player.GuiId);
                }

                PausePlayer(player);
            }
            else if ((player = _players.Values.FirstOrDefault(x => x.GuiId.Equals(id))) != null)
            {
                LogLine("Connetion lost of GUI for player " + player.Name);

                // Notify CU
                if (!player.ControlUnitId.Equals(Guid.Empty))
                {
                    _server.Send(new GameEvent(GameEvent.Type.GuiLeft, null), player.ControlUnitId);
                }

                RemoveGui(player);
            }
            else
            {
                LogLine("Connection lost of unknown connection");
            }
        }

        /// <summary>
        /// Handles the GUI Request
        /// </summary>
        /// <param name="guid"></param>
        /// <param name="playerName"></param>
        private void GuiRequest(Guid guid, string playerName)
        {
            LogLine("Received Gui Request for player " + playerName);

            // Player not existing?
            if (!_players.ContainsKey(playerName))
            {
                LogLine("Player is not existing");

                // Send NOT OK
                _server.Send(new GameEvent(GameEvent.Type.GuiJoined, null), guid);
                return;
            }

            LogLine("Accepted Request");

            // Add GUI-Guid to player
            var player = _players[playerName];
            player.GuiId = guid;

            // Send OK
            var game = new Arctos.Game.Middleware.Logic.Model.Model.Game()
            {
                State = _game.State,
                GameArea = player.Map,
                Path = _game.Path != null ? new Path(_game.Path) : null
            };
            _server.Send(new GameEvent(GameEvent.Type.GuiJoined, game), guid);

            // Send Event
            OnGuiChangedEvent(new GuiChangedEventArgs(_players[playerName]));
        }

        /// <summary>
        /// Handles the player request
        /// </summary>
        /// <param name="guid"></param>
        /// <param name="playerName"></param>
        private void PlayerRequest(Guid guid, string playerName)
        {
            LogLine("Received Player Join Request from " + playerName);

            // Player already existing?
            if (_players.ContainsKey(playerName))
            {
                var existingPlayer = _players[playerName];

                LogLine("Player is already registered");

                // Is the player paused?
                if (!_players[playerName].Pause)
                {
                    LogLine("Player cannot connect because of duplicate usernames");
                    // Send NOT OK
                    _server.Send(
                        new GameEvent(GameEvent.Type.PlayerJoined, new GameEventTuple<bool, string>()
                        {
                            Item1 = false,
                            Item2 = "Username already taken"
                        }),
                        guid);
                    return;
                }

                // Resume the player
                LogLine("Player " + playerName + "can now resume");
                ResumePlayer(existingPlayer);

                // Send OK to CU
                _server.Send(new GameEvent(GameEvent.Type.PlayerJoined, new GameEventTuple<bool, string>()
                {
                    Item1 = true,
                    Item2 = "Player rejoined"
                }), guid);

                // Notify GUI if existing
                if (!existingPlayer.GuiId.Equals(Guid.Empty))
                {
                    _server.Send(new GameEvent(GameEvent.Type.PlayerJoined, null), existingPlayer.GuiId);
                }
                return;
            }

            // Is a map available
            var map = _game.GetAvailableMap();
            if (map == null)
            {
                LogLine("No map available");

                _server.Send(
                    new GameEvent(GameEvent.Type.PlayerJoined, new GameEventTuple<bool, string>()
                    {
                        Item1 = false,
                        Item2 = "No map available"
                    }),
                    guid);
                return;
            }

            LogLine("Accepted Join Request");

            // Add players instance to map
            var player = new Player
            {
                ControlUnitId = guid,
                Name = playerName,
                Map = map
            };

            // Add player
            _players.Add(playerName, player);

            // Send OK
            _server.Send(new GameEvent(GameEvent.Type.PlayerJoined, new GameEventTuple<bool, string>()
            {
                Item1 = true,
                Item2 = "Player joined"
            }), guid);

            // Send Event
            OnPlayerJoinedEvent(new PlayerJoinedEventArgs(player));
        }

        /// <summary>
        /// Handles the AreaUpdate event
        /// </summary>
        /// <param name="controlUnitGuid"></param>
        /// <param name="areaId"></param>
        private void AreaUpdate(Guid controlUnitGuid, string areaId)
        {
            // Find GUI
            var player = FindPlayerByCu(controlUnitGuid);

            // Change player position
            if (player != null)
            {
                LogLine("Received area update from " + player.Name + " at field " + areaId);

                player.UpdatePosition(areaId);

                // Game started?
                if (_game.State == GameState.Started && player.Location != null)
                {
                    var position = player.ChangePositionStatus(areaId);
                    if (position == null)
                    {
                        // Paused
                        return;
                    }

                    if (position.Status.Equals(Area.AreaStatus.CorrectlyPassed))
                    {
                        LogLine("Player has correctly passed the next field in the queue");
                    }
                    else
                    {
                        LogLine("Player has wrongly passed that field");

                        // Add a penalty second
                        player.Duration = player.Duration.Add(new TimeSpan(0, 0, 0, 1));
                    }

                    // Send Update to GUI when connected
                    if (!player.GuiId.Equals(Guid.Empty))
                    {
                        _server.Send(new GameEvent(GameEvent.Type.AreaUpdate, position), player.GuiId);
                    }
                }
            }
        }

        private Player FindPlayerByCu(Guid controlUnitGuid)
        {
            return _players.Values.FirstOrDefault(player => player.ControlUnitId.Equals(controlUnitGuid));
        }

        /// <summary>
        /// Starts the game and with it all timers
        /// </summary>
        public void StartGame()
        {
            // Check if all players are ready
            if (!PlayersReady())
            {
                LogLine("Not all players are ready");
                return;
            }

            LogLine("Starting the game as all players are ready");

            // Send Message to all CUs and GUIs
            _server.Send(new GameEvent(GameEvent.Type.GameStart, true));

            // Start timers
            var startTime = DateTime.Now;
            foreach (var player in _players.Values)
            {
                player.Start(startTime);
            }

            // Start game
            _game.State = GameState.Started;

            // Send event
            OnGameStartEvent(new GameStartEventArgs() {Started = true});
        }

        public void RequestReset()
        {
            LogLine("Resetting the game");

            _game.RequestReset = true;
        }

        private void Reset()
        {
            // Reset the game
            _game.Reset();

            // Reset all players
            foreach (var player in _players.Values)
            {
                player.Reset();

                player.Map = _game.GetAvailableMap();
            }

            // Set the game state
            _game.State = GameState.Waiting;

            // Notify everyone
            _server.Send(new GameEvent(GameEvent.Type.GameReset, null));
        }

        /// <summary>
        /// Checks if all players are on their start field!
        /// </summary>
        /// <returns></returns>
        public bool PlayersReady()
        {
            return _players.Count > 0 &&
                   _players.Values.Count(x => x.Location != null && x.Map != null && x.Location.Equals(x.Map.StartField)) == _players.Count;
        }

        /// <summary>
        /// Sends a new Log Event
        /// </summary>
        /// <param name="log"></param>
        protected void LogLine(string log)
        {
            OnLogEventHandler(new LogEventArgs() {Log = log});
        }

        #region Observer

        public void OnCompleted()
        {
            throw new NotImplementedException();
        }

        public void OnError(Exception error)
        {
            throw new NotImplementedException();
        }

        public void OnNext(Tuple<Guid, GameEvent> value)
        {
            // Receive GameEvent
            _receivedEvents.Enqueue(value);
        }

        #endregion

        #region EventHandlers

        protected virtual void OnPlayerJoinedEvent(PlayerJoinedEventArgs e)
        {
            if (PlayerJoinedEvent != null) PlayerJoinedEvent.Invoke(this, e);
        }

        protected virtual void OnGuiChangedEvent(GuiChangedEventArgs e)
        {
            if (GuiChangedEvent != null) GuiChangedEvent.Invoke(this, e);
        }

        protected virtual void OnGameReadyEvent(GameReadeEventArgs e)
        {
            if (GameReadyEvent != null) GameReadyEvent.Invoke(this, e);
        }

        protected virtual void OnGameStartEvent(GameStartEventArgs e)
        {
            if (GameStartEvent != null) GameStartEvent.Invoke(this, e);
        }

        protected virtual void OnLogEventHandler(LogEventArgs e)
        {
            if (LogEvent != null) LogEvent.Invoke(this, e);
        }

        protected virtual void OnPlayerLeftEvent(PlayerLeftEventArgs e)
        {
            if (PlayerLeftEvent != null) PlayerLeftEvent.Invoke(this, e);
        }

        protected virtual void OnGameFinishedEvent(GameFinishEventArgs e)
        {
            if (GameFinishedEvent != null) GameFinishedEvent.Invoke(this, e);
        }

        protected virtual void OnPlayerFinishedEvent(PlayerFinishedEventArgs e)
        {
            if (PlayerFinishedEvent != null) PlayerFinishedEvent.Invoke(this, e);
        }

        protected virtual void OnPlayerLostEvent(PlayerLostEventArgs e)
        {
            if (PlayerLostEvent != null) PlayerLostEvent.Invoke(this, e);
        }

        #endregion

        #region Events

        public event PlayerJoinedEventHandler PlayerJoinedEvent;
        public event PlayerLeftEventHandler PlayerLeftEvent;
        public event GuiChangedEventHandler GuiChangedEvent;
        public event GameReadyEventHandler GameReadyEvent;
        public event GameStartEventHandler GameStartEvent;
        public event LogEventHandler LogEvent;
        public event GameFinishedEventHandler GameFinishedEvent;
        public event PlayerFinishedEventHandler PlayerFinishedEvent;
        public event PlayerLostEventHandler PlayerLostEvent;

        #endregion
    }
}