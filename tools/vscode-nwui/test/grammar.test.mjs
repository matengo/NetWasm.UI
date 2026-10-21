// Tokenizes .nwui snippets with the real HTML and C# grammars (from tm-grammars) and checks the scopes.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import vsctm from 'vscode-textmate';
import oniguruma from 'vscode-oniguruma';

const require = createRequire(import.meta.url);
const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = join(here, '..', '..', '..');

const grammars = {
  'text.html.nwui': join(here, '..', 'syntaxes', 'nwui.tmLanguage.json'),
  'text.html.basic': require.resolve('tm-grammars/grammars/html.json'),
  'source.cs': require.resolve('tm-grammars/grammars/csharp.json'),
};

const wasm = readFileSync(require.resolve('vscode-oniguruma/release/onig.wasm')).buffer;
const onig = oniguruma.loadWASM(wasm).then(() => ({
  createOnigScanner: (sources) => new oniguruma.OnigScanner(sources),
  createOnigString: (s) => new oniguruma.OnigString(s),
}));

const registry = new vsctm.Registry({
  onigLib: onig,
  loadGrammar: async (scope) => {
    const path = grammars[scope];
    return path ? vsctm.parseRawGrammar(readFileSync(path, 'utf8'), path) : null;
  },
});

const grammar = await registry.loadGrammar('text.html.nwui');

/** Returns [text, scopes] for every token of the snippet. */
function tokenize(source) {
  const tokens = [];
  let stack = vsctm.INITIAL;
  for (const line of source.split('\n')) {
    const r = grammar.tokenizeLine(line, stack);
    for (const t of r.tokens) {
      tokens.push([line.substring(t.startIndex, t.endIndex), t.scopes]);
    }
    stack = r.ruleStack;
  }
  return tokens;
}

/** Asserts that the first token whose text is `text` (after `after`, if given) has a scope containing `scope`. */
function expectScope(tokens, text, scope, after) {
  let start = 0;
  if (after) {
    start = tokens.findIndex(([t]) => t.trim() === after) + 1;
    assert.ok(start > 0, `no token '${after}'`);
  }
  const token = tokens.slice(start).find(([t]) => t.trim() === text);
  assert.ok(token, `no token '${text}' in ${JSON.stringify(tokens.map(([t]) => t))}`);
  assert.ok(
    token[1].some((s) => s.includes(scope)),
    `'${text}' has ${JSON.stringify(token[1])}, expected '${scope}'`);
}

function expectNoScope(tokens, text, scope) {
  const token = tokens.find(([t]) => t.trim() === text);
  assert.ok(token, `no token '${text}'`);
  assert.ok(!token[1].some((s) => s.includes(scope)), `'${text}' should not have '${scope}': ${JSON.stringify(token[1])}`);
}

test('html stays html', () => {
  const t = tokenize('<div class="x">Hi &amp; bye <!-- c --></div>');
  expectScope(t, 'div', 'entity.name.tag.html');
  expectScope(t, 'class', 'entity.other.attribute-name');
  expectScope(t, 'amp', 'constant.character.entity');
  expectScope(t, 'c', 'comment.block.html');
});

test('razor comments and @@ escapes', () => {
  const t = tokenize('<p>@* note *@ me@example.com @@home</p>');
  expectScope(t, 'note', 'comment.block.nwui');
  expectScope(t, '@@', 'constant.character.escape.nwui');
  expectNoScope(t, 'me@example.com', 'source.cs');
});

test('implicit and explicit expressions in text', () => {
  const t = tokenize('<p>Hello @user.Name! @(count.Value * 2) @Items[0].Title</p>');
  expectScope(t, 'user', 'source.cs');
  expectScope(t, 'Name', 'source.cs');
  expectNoScope(t, '!', 'source.cs');
  expectScope(t, '*', 'keyword.operator.arithmetic.cs');
  expectScope(t, 'Title', 'source.cs');
});

test('expressions inside html attribute values', () => {
  const t = tokenize('<p title="Count is @count.Value" data-x="@(a ? "b" : "c")">x</p>');
  expectScope(t, 'count', 'source.cs', 'Count is');
  expectScope(t, '?', 'keyword.operator.conditional');
  expectScope(t, 'b', 'string.quoted.double.cs');
});

test('directive attributes are C#', () => {
  const t = tokenize('<button @onclick="() => count.Value++" @onclick:preventDefault @ref="button">+</button>');
  expectScope(t, 'onclick', 'entity.other.attribute-name.directive.nwui');
  expectScope(t, '=>', 'source.cs');
  expectScope(t, '++', 'keyword.operator.increment.cs');
  expectScope(t, 'preventDefault', 'entity.other.attribute-name.modifier.nwui');
  expectScope(t, 'button', 'source.cs', 'ref');
});

test('control flow', () => {
  const t = tokenize([
    '<div>',
    '    @if (Level.Value > 3) {',
    '        <em>hot</em>',
    '    } else if (x) {',
    '        <i>x</i>',
    '    } else {',
    '    }',
    '    @foreach (var item in Items) {',
    '        <li @key="item.Id">@item.Title</li>',
    '    }',
    '</div>',
  ].join('\n'));
  expectScope(t, 'if', 'keyword.control.conditional.if.cs');
  expectScope(t, '>', 'keyword.operator.relational.cs', 'Value');
  expectScope(t, 'em', 'entity.name.tag.html');
  expectScope(t, 'else', 'keyword.control.conditional.else.cs');
  expectScope(t, 'foreach', 'keyword.control.loop.foreach.cs');
  expectScope(t, 'var', 'storage.type.var.cs');
  expectScope(t, 'in', 'keyword.control.loop.in.cs');
  expectScope(t, 'li', 'entity.name.tag.html');
  expectScope(t, 'Title', 'source.cs');
});

test('components, props and slots', () => {
  const t = tokenize('<Card Title="Hi @name.Value" Open><template slot="Header">h</template>@render(ChildContent)</Card>');
  expectScope(t, 'Card', 'entity.name.type.class.component.nwui');
  expectScope(t, 'Title', 'entity.other.attribute-name');
  expectScope(t, 'name', 'source.cs');
  expectScope(t, 'Open', 'entity.other.attribute-name');
  expectScope(t, 'template', 'entity.name.tag.html');
  expectScope(t, 'render', 'keyword.control.nwui.directive');
  expectScope(t, 'ChildContent', 'source.cs');
});

test('top-level directives and @code', () => {
  const t = tokenize([
    '@using NetWasm.UI.Dom',
    '@namespace My.App',
    '<b>@Level</b>',
    '@prop IReadOnlyList<string> Tags = new string[0];',
    '@code {',
    '    private readonly Signal<int> count = new(0);',
    '    public void Bump() { if (count.Value > 0) { count.Value++; } }',
    '}',
    '<p>after</p>',
  ].join('\n'));
  expectScope(t, 'using', 'keyword.control.nwui.directive');
  expectScope(t, 'NetWasm.UI.Dom', 'entity.name.type.namespace');
  expectScope(t, 'prop', 'keyword.control.nwui.directive');
  expectScope(t, 'IReadOnlyList', 'entity.name.type');
  expectScope(t, 'Tags', 'entity.name.variable.property');
  expectScope(t, 'new', 'keyword.operator.expression.new.cs', 'Tags');
  expectScope(t, 'readonly', 'storage.modifier');
  expectScope(t, 'Bump', 'entity.name.function');
  expectScope(t, 'p', 'entity.name.tag.html');
  expectNoScope(t, 'after', 'source.cs');
});

test('every .nwui file in the repo tokenizes and leaves C# mode at the end', () => {
  const files = [];
  const walk = (dir) => {
    for (const name of readdirSync(dir)) {
      if (['node_modules', 'bin', 'obj', '.git'].includes(name)) continue;
      const path = join(dir, name);
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith('.nwui')) files.push(path);
    }
  };
  walk(repoRoot);
  assert.ok(files.length >= 5, `found ${files.length} .nwui files`);
  for (const file of files) {
    const t = tokenize(readFileSync(file, 'utf8').replace(/\r/g, '').trimEnd() + '\n<z>end</z>');
    const end = t.findLast(([text]) => text === 'end');
    assert.deepEqual(end[1], ['text.html.nwui'], `${file}: ${JSON.stringify(end[1])}`);
  }
});
