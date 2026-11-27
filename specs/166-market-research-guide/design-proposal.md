# Market research guide

Status: Gate A: Approved by Kamran Abdi on 2026-11-27

## Product design

Add a Persian RTL page named «راهنمای تحلیل و رصد بازار» to Sapio at `/market-guide`. Make it a public, responsive product guide, following the standalone route pattern used by `/releases`. Add a discoverable entry in the desktop workspace navigation and the mobile account menu.

Owner amendment (2026-11-27) and subsequent UI review: on the public home page (`/`), place «راهنمای تحلیل و رصد بازار» as a secondary text link directly beneath the product introduction. The introduction precedes the sign-in card on mobile and sits alongside it on desktop. The guide opens without sign-in. The homepage link is independent of the workspace navigation entries.

The first screen pairs the page title with an original Sapio monthly-sales answer. Below it, a gallery groups features by production and sales, valuation, financial statements, deep analysis, and watchlist and access. Selecting a feature presents a short purpose, a sample question, and its original screenshot inline. A keyboard-accessible image viewer opens the full-resolution capture and supports panning at original size.

Use the existing Dastkhat font, Sapio brand mark, light and dark themes, and product navigation components. Share the same layout across devices. On small screens the topic and feature controls wrap above the image, with tap targets of at least 44 px.

## Example behavior

The supplied captures cover 14 examples. The deep-analysis capture shows the controls, and the watchlist, events, and support captures show their respective product screens. Label those images accordingly. The Telegram entry explains connection and gives an example question. It can link to the existing connection flow. A Telegram screenshot can be added when an authentic capture is available.

Where the example is a chat prompt, offer question copying and a way to open the matching Sapio composer with the prompt prefilled. Require the normal explicit send action. Where the feature is a workflow, link to the corresponding existing screen. The guide itself requires no financial API call.

## Dependencies and checks

Use optimized local derivatives for inline previews and serve the original screenshots only when needed. Reuse the existing routing, theme, and dialog patterns. Verify the page as a guest and a signed-in user, test the gallery and image viewer with keyboard and touch, and check mobile and desktop light and dark views. Verify that opening a sample leaves the question in the composer and that only the user's send action can run it.

The standalone design preview is `docs/help/sapio-feature-guide-fa.html`. The separate new-chat introduction follows its existing approval record.
