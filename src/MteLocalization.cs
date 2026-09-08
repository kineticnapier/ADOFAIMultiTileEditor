using System.Collections.Generic;
using KineticNapier.ADOFAIWorkbench;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class MteLocalization
    {
        private const string Owner = "adofai.mte";
        private static bool initialized;

        internal static void Initialize()
        {
            if (initialized) return;
            initialized = true;

            WorkbenchLocalization.Register(Owner, "en-US", "English", new Dictionary<string, string>
            {
                { "tracks.title", "MTE Tracks" },
                { "tracks.heading", "MTE Tracks" },
                { "tracks.new", "New track" },
                { "tracks.store", "+ Store current" },
                { "tracks.empty", "No tracks yet. Select the Multi Tile start floor, then store the current chart." },
                { "tracks.delete", "Delete" },
                { "tracks.meta", "Start F{0}   Cursor F{1}   {2}" },
                { "editor.open", "Open the ADOFAI level editor first." },
                { "settings.title", "Multi Tile" },
                { "settings.heading", "Multi Tile Editor v{0}" },
                { "editor.inactive", "Level editor is not active." },
                { "activeSource", "Active source" },
                { "track", "Track" },
                { "source.meta", "Start F{0}   Cursor F{1}   {2}   {3}" },
                { "planetA", "Planet A" },
                { "planetB", "Planet B" },
                { "initialPivot", "Initial pivot: {0}" },
                { "saveTrack", "Save track" },
                { "setStart", "Set start from selection" },
                { "layout", "Layout" },
                { "off", "Off" },
                { "tiles", "Tiles" },
                { "beats", "Beats" },
                { "length", "Length" },
                { "tiles.unit", "tiles" },
                { "beats.unit", "beats" },
                { "virtualRepeat", "Virtual repeat" },
                { "reuse", "Return to first tile / reuse one source cycle" },
                { "layout.offDescription", "Layout folding is off; Position Track and virtual repeat returns still use instant planet teleports." },
                { "detached", "Generated output is detached. Choose a track in MTE Tracks to continue editing a source track." },
                { "chooseStart", "Choose the Multi Tile start floor, then store the current chart in MTE Tracks." },
                { "generation", "Generation" },
                { "state.notAnalyzed", "Not analyzed" },
                { "state.analyzed", "Analyzed" },
                { "state.ready", "Ready" },
                { "analyzeVerify", "Analyze + Verify" },
                { "generate", "Generate Multi Tile" },
                { "clear", "Clear" },
                { "status", "Status: " },
                { "error", "ERROR: " },
                { "showAdvanced", "Show advanced / diagnostics" },
                { "hideAdvanced", "Hide advanced / diagnostics" },
                { "analyzeOnly", "Analyze only" },
                { "verifyOnly", "Verify only" },
                { "fullDiagnostic", "Full diagnostic:" },
                { "angleUnknown", "angle ?" },
                { "angles", "{0} angles" },
                { "empty", "empty" },
                { "planSummary", "Start F{0}   Tracks {1}   Duration {2} sec   Master {3} BPM   Layout/repeat per group" },
                { "initialStatus", "Select the floor where Multi Tile should begin, then store each source chart as a track." },
                { "paging.title", "MTE Paging" },
                { "paging.heading", "Dynamic paging" },
                { "paging.help", "For dense Hz charts, split each generated Floor group into reusable pages. Inactive pages stay off-screen and zero-duration MoveDecorations swaps pages at page boundaries." },
                { "paging.empty", "No MTE tracks are stored." },
                { "paging.enabled", "Paged" },
                { "paging.pageSize", "Page size" },
                { "paging.tilesPerPage", "tiles / page" },
                { "paging.note", "Paged mode forces the normal Off/Tiles/Beats layout to Off. Page-boundary tiles are duplicated so the landing tile remains visible during the swap." },
                { "paging.apply", "Apply paging to generated output" },
                { "paging.queued", "Paging update queued." },
                { "paging.savedForGenerate", "Paging settings saved. Generate Multi Tile to apply them to the output." },
                { "paging.disabledOutput", "Paging is disabled. Regenerate Multi Tile to restore a normal output layout." },
                { "paging.enabledQueued", "Paged mode enabled; it will apply after generation or to the detached output." },
                { "paging.disabledQueued", "Paged mode disabled; regenerate to remove paging from an existing output." },
                { "paging.sizeQueued", "Page size updated." }
            });

            WorkbenchLocalization.Register(Owner, "ja-JP", "日本語", new Dictionary<string, string>
            {
                { "tracks.title", "MTEトラック" },
                { "tracks.heading", "MTEトラック" },
                { "tracks.new", "新規トラック" },
                { "tracks.store", "+ 現在を保存" },
                { "tracks.empty", "トラックがありません。Multi Tileの開始タイルを選択して、現在の譜面を保存してください。" },
                { "tracks.delete", "削除" },
                { "tracks.meta", "開始 F{0}   カーソル F{1}   {2}" },
                { "editor.open", "先にADOFAIのレベルエディタを開いてください。" },
                { "settings.title", "Multi Tile" },
                { "settings.heading", "Multi Tile Editor v{0}" },
                { "editor.inactive", "レベルエディタが開かれていません。" },
                { "activeSource", "編集中のソース" },
                { "track", "トラック" },
                { "source.meta", "開始 F{0}   カーソル F{1}   {2}   {3}" },
                { "planetA", "惑星 A" },
                { "planetB", "惑星 B" },
                { "initialPivot", "初期中心: {0}" },
                { "saveTrack", "トラックを保存" },
                { "setStart", "選択位置を開始地点にする" },
                { "layout", "レイアウト" },
                { "off", "オフ" },
                { "tiles", "タイル" },
                { "beats", "拍" },
                { "length", "長さ" },
                { "tiles.unit", "タイル" },
                { "beats.unit", "拍" },
                { "virtualRepeat", "仮想リピート" },
                { "reuse", "先頭タイルへ戻る / 1周期の配置を再利用" },
                { "layout.offDescription", "レイアウト折り返しはオフです。Position Trackと仮想リピートの復帰では惑星を瞬間移動します。" },
                { "detached", "生成結果はソースから切り離されています。編集を再開するにはMTEトラックからトラックを選択してください。" },
                { "chooseStart", "Multi Tileの開始タイルを選択して、現在の譜面をMTEトラックへ保存してください。" },
                { "generation", "生成" },
                { "state.notAnalyzed", "未解析" },
                { "state.analyzed", "解析済み" },
                { "state.ready", "生成可能" },
                { "analyzeVerify", "解析 + 検証" },
                { "generate", "Multi Tileを生成" },
                { "clear", "クリア" },
                { "status", "状態: " },
                { "error", "エラー: " },
                { "showAdvanced", "詳細 / 診断を表示" },
                { "hideAdvanced", "詳細 / 診断を隠す" },
                { "analyzeOnly", "解析のみ" },
                { "verifyOnly", "検証のみ" },
                { "fullDiagnostic", "詳細診断:" },
                { "angleUnknown", "角度 ?" },
                { "angles", "{0}角度" },
                { "empty", "空" },
                { "planSummary", "開始 F{0}   トラック {1}   長さ {2}秒   Master {3} BPM   グループ別レイアウト/リピート" },
                { "initialStatus", "Multi Tileを開始するタイルを選択して、各ソース譜面をトラックとして保存してください。" },
                { "paging.title", "MTEページング" },
                { "paging.heading", "動的ページング" },
                { "paging.help", "高密度なHz譜面向けに、生成されたFloor装飾を再利用可能なページへ分割します。非表示ページは画面外へ退避し、ページ境界で0秒のMoveDecorationsを使って入れ替えます。" },
                { "paging.empty", "保存されたMTEトラックがありません。" },
                { "paging.enabled", "ページング" },
                { "paging.pageSize", "ページ長" },
                { "paging.tilesPerPage", "タイル / ページ" },
                { "paging.note", "ページング中は通常のオフ/タイル/拍レイアウトをオフに固定します。境界タイルを複製するため、切り替え時も着地点が消えません。" },
                { "paging.apply", "生成結果へページングを適用" },
                { "paging.queued", "ページング更新を予約しました。" },
                { "paging.savedForGenerate", "ページング設定を保存しました。Multi Tile生成時に適用されます。" },
                { "paging.disabledOutput", "ページングは無効です。既存の生成結果を通常配置へ戻すには再生成してください。" },
                { "paging.enabledQueued", "ページングを有効にしました。生成後、または切り離された生成結果へ適用されます。" },
                { "paging.disabledQueued", "ページングを無効にしました。既存の生成結果から外すには再生成してください。" },
                { "paging.sizeQueued", "ページ長を更新しました。" }
            });

            WorkbenchLocalization.LanguageChanged += delegate
            {
                WorkbenchIntegration.RefreshLanguage();
            };
        }

        internal static string T(string key, string fallback)
        {
            return WorkbenchLocalization.T(Owner, key, fallback);
        }

        internal static string F(string key, string fallback, params object[] args)
        {
            return WorkbenchLocalization.Format(Owner, key, fallback, args);
        }
    }
}
