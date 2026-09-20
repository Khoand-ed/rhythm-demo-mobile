using System;
using System.Collections.Generic;
using Tools;
using UnityEngine;

namespace Data.Player {
    public class PlayerManager : Single<PlayerManager> {
        
        private List<PlayerData> list = new List<PlayerData>();
        
        private PlayerData playerData;

        public PlayerManager() {
            //资源缺失时 Asset.Load 返回 null, 不能塞进列表, 否则 Login 会空引用
            //Asset.Load returns null when the data is missing; adding those would make Login
            //throw on data.GetName(). Asset.Load already reports what it could not find.
            Add(Asset.Load<PlayerData>("Data/User", "Saukiya"));
            Add(Asset.Load<PlayerData>("Data/User", "Test"));
        }

        private void Add(PlayerData data) {
            if (data != null) list.Add(data);
        }

        public PlayerData Get() {
            return playerData;
        }

        public PlayerData Login(string name,string password) {
            playerData = null;
            foreach (PlayerData data in list) {
                if (data == null) continue;
                if (data.GetName() == name && data.GetPassword() == password) {
                    playerData = data;
                    //Resources 里的资产只是初始值, 存档盖在上面 / The seeded asset is only the
                    //starting point; a save, once one exists, is authoritative over it.
                    //No file yet means a first login, and Load leaves the seed alone.
                    PlayerSave.Load(data);
                    data.ItemSort();
                    break;
                }
            }
            return playerData;
        }

        public void Register(string name,string password) {
            if (list.Find(data => data.name == name) != null) {
                throw new Exception("That username is already taken.");
            }
            PlayerData playerData = ScriptableObject.CreateInstance<PlayerData>();
            playerData.Initialization(name, password);
            list.Add(playerData);
        }

        /// <summary>
        /// 改完物品/理智后存档 / Persists the logged-in player. Call this after any change
        /// that has to survive a restart - PlayerData is a read-only Resources asset on a
        /// device, so an unsaved mutation is gone at the next launch.
        /// </summary>
        public void Save() {
            if (playerData == null) return;
            PlayerSave.Save(playerData);
        }

        public void Exit() {
            playerData = null;
        }
    }
}