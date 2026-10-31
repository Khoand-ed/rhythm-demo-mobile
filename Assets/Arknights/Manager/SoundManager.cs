using System;
using System.Collections;
using DG.Tweening;
using Settings;
using Tools;
using UnityEngine;

namespace Manager {
    public class SoundManager : MonoSingle<SoundManager> {

        private AudioSource soundEffect;
        private AudioSource music;
        private AudioSource voice;
        private Coroutine cor;

        // 淡入淡出只动这个系数 / What the cross-fades move: a 0-1 factor, never the volume itself. The
        // music's volume is always this times the player's setting (ApplyMusicVolume), so no fade and
        // no new clip can play it louder than the settings screen says. Both used to write the
        // volume straight from the slider's value, whether or not Music was switched off.
        private float musicFade = 1f;

        protected override void Initialization() {
            soundEffect = AddAudio("SoundEffect");
            music = AddAudio("Music");
            voice = AddAudio("Voice");

            // 先读存档再应用 / The saved settings first, so the very first clip plays at them.
            GameSettings.LoadSound();
            UpdateData();
        }

        public AudioSource AddAudio(string name = "AudioSource") {
            GameObject gameObject = new GameObject(name);
            gameObject.transform.parent = transform;
            return gameObject.AddComponent<AudioSource>();
        }

        // 播放音频 （参数：音频，是否循环，回调函数）
        public void PlayMusic(AudioClip clip, bool loop, Action action = null) {
            if (cor != null) {//停止之前的携程
                StopCoroutine(cor);
                cor = null;
            }
            // 上一次的淡入淡出也停掉 / The last call's fades go too, like its coroutine above. One still
            // running would finish after this call - playing its own clip over this one, starting its
            // own intro-to-loop wait, or (StopMusic's) stopping this clip a second after it began.
            DOTween.Kill(music);
            //没有音频资源时直接返回, 否则协程里的 clip.length 会空引用
            //Bail out on a missing clip: startYingPingJiShi would NRE on clip.length.
            if (clip == null) {
                music.Stop();
                music.clip = null;
                return;
            }
            if (!loop && GameSettings.SOUND_MUSIC_ENABLE) {// 如果不是循环并且音乐频道为开启，则是淡入淡出播放
                FadeMusic(0, 1).OnComplete(() => {
                    FadeMusic(1, 1);
                    music.clip = clip;
                    music.loop = false;
                    music.Play();
                    if (action != null) {//如果存在回调函数，则创建协程 -- 因为延迟函数所以要多些一行
                        cor = StartCoroutine(startYingPingJiShi(clip, action));
                    }
                });
            } else {//循环模式则直接切歌
                // 音量照设置来 / At whatever the settings allow - nothing while Music is off. This line
                // used to set the slider's value outright, so the loop an intro hands over to started
                // up loud minutes after Music was switched off, and so did Home's music on every return
                // to it. The clip still plays, silently, so switching Music back on picks it up.
                musicFade = 1f;
                ApplyMusicVolume();
                music.clip = clip;
                music.loop = loop;
                music.Play();
                if (action != null) {//如果存在回调函数，则创建协程
                    cor = StartCoroutine(startYingPingJiShi(clip, action));
                }
            }
        }

        // 播放完后音频回调函数
        IEnumerator startYingPingJiShi(AudioClip clip, Action action) {
            yield return  new WaitForSeconds(clip.length);
            cor = null;//清理携程变量
            action();
        }

        public void PlayEffect(AudioClip clip) {
            if (clip == null) return;
            soundEffect.PlayOneShot(clip);
        }

        public void StopMusic() {
            // 停就是全停 / Stopping stops what was due next as well: an intro's wait to hand over to
            // its loop. Left running, it started the front-end's loop by itself once the intro's
            // length had passed - in the middle of a song, since entering one is what calls this.
            if (cor != null) {
                StopCoroutine(cor);
                cor = null;
            }
            DOTween.Kill(music);

            FadeMusic(0, 1).OnComplete(() => {
                music.Stop();
            });
        }

        public void UpdateData() {
            soundEffect.volume = GameSettings.SOUND_SOUND_EFFECT_ENABLE ? GameSettings.SOUND_SOUND_EFFECT_VALUE / 100f : 0;
            ApplyMusicVolume();
            voice.volume = GameSettings.SOUND_VOICE_ENABLE ? GameSettings.SOUND_VOICE_VALUE / 100f : 0;
        }

        /// <summary>
        /// 音乐音量只在这里写 / The one place the music's volume is written: the fade factor times the
        /// player's setting, 0 while Music is off. A fade tick, a new clip and the settings screen all
        /// come through here, so a change made mid-fade takes effect at once instead of being
        /// overwritten by the fade's end value.
        /// </summary>
        private void ApplyMusicVolume() {
            float setting = GameSettings.SOUND_MUSIC_ENABLE ? GameSettings.SOUND_MUSIC_VALUE / 100f : 0f;
            music.volume = musicFade * setting;
        }

        /// <summary>
        /// 淡入淡出 / Moves the fade factor to <paramref name="to"/>. Tagged with the music source as its
        /// target, so the next PlayMusic or StopMusic can kill it with DOTween.Kill(music).
        /// </summary>
        private Tween FadeMusic(float to, float duration) {
            return DOTween.To(() => musicFade, value => {
                musicFade = value;
                ApplyMusicVolume();
            }, to, duration).SetTarget(music);
        }
    }
}