using System;
using System.Threading.Tasks;
using Data.Player;
using Promuse.Contracts.Mail;
using Promuse.Net;
using Tools;

namespace Data.HomeContent {
    /// <summary>
    /// 邮箱 / The player's mailbox as the server holds it. Every action answers with the box and the
    /// whole player, which are applied here; the screens only draw.
    /// </summary>
    public class MailManager : Single<MailManager> {
        public MailBox Box { get; private set; }

        /// <summary>New mails - the dot on Home's mail icon.</summary>
        public int Unopened => Box != null ? Box.Unopened : 0;

        public event Action Changed;

        public async Task<ApiResult<MailBox>> LoadAsync() {
            ApiResult<MailBox> result = await PlayerManager.Inst().Api.GetMailAsync();
            if (result.IsSuccess) Set(result.Value);
            return result;
        }

        public Task<ApiResult<MailActionResult>> OpenAsync(Guid mailId) =>
            Apply(PlayerManager.Inst().Api.OpenMailAsync(mailId));

        public Task<ApiResult<MailActionResult>> CollectAllAsync() =>
            Apply(PlayerManager.Inst().Api.CollectAllMailAsync());

        public Task<ApiResult<MailActionResult>> DeleteOpenedAsync() =>
            Apply(PlayerManager.Inst().Api.DeleteOpenedMailAsync());

        private async Task<ApiResult<MailActionResult>> Apply(Task<ApiResult<MailActionResult>> call) {
            ApiResult<MailActionResult> result = await call;
            if (!result.IsSuccess) return result;

            PlayerData player = PlayerManager.Inst().Get();
            if (player != null) player.ApplyServerState(result.Value.Player);
            Set(result.Value.Box);
            return result;
        }

        private void Set(MailBox box) {
            Box = box;
            Changed?.Invoke();
        }
    }
}
