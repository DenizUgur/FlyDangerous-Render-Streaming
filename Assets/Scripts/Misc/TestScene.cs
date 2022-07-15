using System.Collections;
using System.IO;
using Cinemachine;
using Core;
using Core.MapData;
using Core.Player;
using Core.Replays;
using Core.Scores;
using Core.ShipModel;
using Gameplay;
using MapMagic.Core;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
#if !NO_PAID_ASSETS
using GPUInstancer;
#endif

namespace Misc {
    /**
     * Simple helper class used to get a test environment with a playable ship and working network
     * without having to go through the menus etc.
     */
    public class TestScene : MonoBehaviour {
        public bool shouldShowTestShip;
        public bool shouldRecordSession;
        public bool shouldReplaySession;
        public Transform spawnLocation;
        public ShipGhost shipGhostPrefab;

        private void Awake() {
            // load engine if not already 
            if (!FindObjectOfType<Engine>()) SceneManager.LoadScene("Engine", LoadSceneMode.Additive);
        }

        private void Start() {
            IEnumerator StartGame() {
                // allow game state to initialise
                yield return new WaitForEndOfFrame();

#if !NO_PAID_ASSETS
                // gpu instancer fun (paid asset!)
                var cam = FindObjectOfType<CinemachineBrain>().gameObject.GetComponent<Camera>();
                GPUInstancerAPI.SetCamera(cam);
#endif

                // instruct the server to create a ship player immediately on start
                Game.Instance.SessionStatus = SessionStatus.SinglePlayerMenu;

                // start server and connect to it
                NetworkServer.dontListen = true;
                FdNetworkManager.Instance.StartHost();

                var level = Level.CrestLoop;
                FdNetworkManager.Instance.StartGameLoadSequence(SessionType.Singleplayer, level.Data);
                Game.Instance.loadedMainLevel = level;
            }

            StartCoroutine(StartGame());
        }

        private void OnApplicationQuit() {
            if (shouldRecordSession) {
                var recorder = GetComponent<ReplayRecorder>();
                recorder.StopRecording();
                recorder.Replay?.Save(new ScoreData());
            }
        }

        private void CreateTestSecondShip() {
            var player = FdPlayer.FindLocalShipPlayer;
            if (player) {
                Instantiate(shipGhostPrefab, player.transform.position + new Vector3(0, 0, 10), Quaternion.identity);
                var targettingSystem = FindObjectOfType<TargettingSystem>();
                if (targettingSystem) targettingSystem.ResetTargets();
            }
        }
    }
}