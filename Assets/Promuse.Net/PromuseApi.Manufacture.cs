#nullable enable

using System;
using System.Threading.Tasks;
using Promuse.Contracts.Manufacture;
using UnityEngine.Networking;

namespace Promuse.Net
{
    /// <summary>
    /// 制造站 / Manufacture's three calls, kept in a file of their own so the feature's whole surface on
    /// the wire reads in one place. Same rules as every other call here: the two that change the bag
    /// carry an idempotency key, and whatever save comes back becomes the ETag held for the next write.
    /// </summary>
    public sealed partial class PromuseApi
    {
        /// <summary>
        /// The player's stations and what each holds - or, before Manufacture Blueprint I, the lock and
        /// the stage that opens it. Answers FEATURE_DISABLED while Manufacture is switched off.
        /// </summary>
        public async Task<ApiResult<ManufactureView>> GetManufactureAsync()
        {
            ApiResult<ManufactureView> result = await SendAsync<ManufactureView>(
                UnityWebRequest.kHttpVerbGET, "/v1/manufacture", body: null, authenticate: true);

            // 补发会改包 / A read that handed out a blueprint owed for an old clear changed the bag, and
            // says so by carrying the save; its version is the one the next write must name.
            if (result.IsSuccess && result.Value!.Player != null) _playerETag = ETagFor(result.Value.Player);

            return result;
        }

        /// <summary>
        /// 收 / Collects one station, or every station that has something when <paramref name="stationId"/>
        /// is null. Idempotent, because it pays out.
        /// </summary>
        public async Task<ApiResult<ManufactureActionResult>> CollectManufactureAsync(string? stationId)
        {
            ApiResult<ManufactureActionResult> result = await SendAsync<ManufactureActionResult>(
                UnityWebRequest.kHttpVerbPOST,
                "/v1/manufacture/collect",
                new CollectRequest(stationId),
                authenticate: true,
                idempotencyKey: Guid.NewGuid().ToString());

            if (result.IsSuccess) _playerETag = ETagFor(result.Value!.Player);

            return result;
        }

        /// <summary>
        /// 升级 / Raises a station from <paramref name="fromLevel"/> - the level the screen showed - to the
        /// next. A screen that has fallen behind is answered STATE_CONFLICT, not raised twice.
        /// Idempotent, because it spends.
        /// </summary>
        public async Task<ApiResult<ManufactureActionResult>> UpgradeStationAsync(string stationId, int fromLevel)
        {
            ApiResult<ManufactureActionResult> result = await SendAsync<ManufactureActionResult>(
                UnityWebRequest.kHttpVerbPOST,
                "/v1/manufacture/upgrade",
                new UpgradeRequest(stationId, fromLevel),
                authenticate: true,
                idempotencyKey: Guid.NewGuid().ToString());

            if (result.IsSuccess) _playerETag = ETagFor(result.Value!.Player);

            return result;
        }
    }
}
