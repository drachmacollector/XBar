# XBar: lift WhatsApp Web composer

A tiny browser extension (Brave / Chrome / Edge) that applies **only on web.whatsapp.com**.
With XBar's overlay the browser covers the whole screen, so WhatsApp's message box sits under the floating taskbar.
This lifts just that box; the page, and XBar, are untouched.

Install: open `brave://extensions`, turn on **Developer mode**, click **Load unpacked**, choose this folder.

If the box is still partly covered (bigger pills / float), raise `TASKBAR_PX` in `content.js` and reload the extension.
It does nothing when Windows reserves the taskbar strip (overlay off).
Bottom taskbar only.
