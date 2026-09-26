/* The on-demand modules (content/app/lazy.js): each by name, the files that make it, in order, and
 * what it needs loaded first. A data script, so one list serves both readers: the page (lazy.js asks
 * for a module by name) and the background (background.js lands the files in the page's isolated
 * world, and loads nothing outside this list).
 *
 * The source manifest names the same files in a content-script group whose match never fires
 * (https://lazy.simplcourses.invalid/*): Safari and Firefox parse none of them until asked, and the
 * iPhone app, with no scripting API, injects that group whole. The Chrome builds leave the group out
 * — Chrome lists a content script's host among the sites the extension reads, and a new site at an
 * update switches every installed copy off until its owner accepts it (scripts/chrome-manifest.py)
 * — and load from this list alone. scripts/dev/api-test.mjs holds the group and this list equal. */
self.BCV_LAZY_MODULES = {
  quiz: { files: ['content/app/quiz-page.js', 'content/app/screens/quiz.js'] },
  submit: { files: ['content/app/screens/submit.js', 'content/app/screens/feedback.js'] },
  hub: { files: ['content/app/hub.js'] }, // the search box's commands, answers and row actions: loaded when the box is focused
  widgets: { files: ['content/app/tools/widgets.js'] }, // widgets of your own: the frame, the importer (loaded by Tools, and by the tray when one is kept)
  starters: { files: ['content/app/tools/widget-starters.js'] }, // the example widgets the importer offers
  'tool:cite': { files: ['content/app/tools/cite.js'] },
  'tool:fc': { files: ['content/app/tools/cards.js'] },
  'tool:conv': { files: ['content/app/tools/convert.js'] },
  'tool:need': { files: ['content/app/tools/need.js'] },
  'tool:pdfx': { files: ['content/app/tools/pdfs.js'] },
  'tool:mark': { files: ['content/app/tools/mark.js'] },
  'tool:ocr': { files: ['content/app/tools/ocr.js'] },
  'tool:ptable': { files: ['content/app/tools/ptable-data.js', 'content/app/tools/ptable.js'] },
  setupcss: { files: ['setup/setup-css.js'] }, // the setup's stylesheet: the setup, Personalize and What's New draw with it
  setup: { needs: ['setupcss'], files: ['content/app/personalize.js', 'content/app/setup.js'] },
  notes: { needs: ['setupcss'], files: ['content/app/whatsnew-notes.js'] },
  phone: { files: ['content/app/phone.js'] },
};
