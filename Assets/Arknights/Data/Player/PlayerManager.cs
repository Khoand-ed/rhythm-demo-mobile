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

        public void Exit() {
            playerData = null;
        }
    }
}