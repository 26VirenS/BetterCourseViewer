// The smoke suite's sections, in shards that run side by side (test-all.mjs; smoke-test.mjs --shard
// <name>). Each shard is a run of its own — its own mock Canvas on its own ports, its own browser
// and profile — of neighbouring sections in their written order, so a section still finds what the
// sections before it in its shard left behind. A section belongs to exactly one shard (the suite's
// harness check fails otherwise). Keep shards near a minute and a half each: test-all.mjs starts
// the longest first, and the slowest shard is the floor under the whole run.
export const SMOKE_SHARDS = {
  start: ['install', 'bundles', "what's new notes", 'dashboard', 'courses', 'to do', 'to do: own tasks + priority', 'calendar', 'inbox'],
  work: ['grades panel', 'submission', 'groups'],
  course: ['course', 'grades stay fresh', 'quiz focus', 'quiz flow', 'quiz feedback'],
  quizzes: ['restricted quiz', 'survey', 'quiz: matching and blanks', 'native pages', 'search'],
  hub: ['search hub'],
  look: ['appearance', 'background loading', 'motion', 'the look off and on'],
  setup: ['guided setup'],
  widgets: ['tools', 'widgets'],
  cards: ['converters and cards', 'quick menus'],
  tools: ['grade needed', 'widgets on a tool tab', 'resilience'],
  unstuck: ['getting unstuck', 'notifications', 'springs'],
  pages: ["what's new", 'setup page', 'account panel', 'extension pages', 'setup cannot be skipped', 'developer', 'reset'],
};

// the two halves as they were before the shards (smoke-test.mjs --part 1|2), for a run by hand
export const SMOKE_PARTS = {
  1: ['install', 'bundles', "what's new notes", 'dashboard', 'courses', 'to do', 'to do: own tasks + priority', 'calendar', 'inbox', 'grades panel', 'submission', 'groups', 'course', 'grades stay fresh', 'quiz focus', 'quiz flow', 'quiz feedback', 'restricted quiz', 'survey', 'quiz: matching and blanks', 'native pages', 'search', 'search hub', 'appearance', 'background loading', 'motion', 'the look off and on'],
  2: ['guided setup', "what's new", 'setup page', 'account panel', 'tools', 'widgets', 'converters and cards', 'grade needed', 'quick menus', 'widgets on a tool tab', 'resilience', 'getting unstuck', 'notifications', 'springs', 'extension pages', 'setup cannot be skipped', 'developer', 'reset'],
};
