"""A light C# reader and the HISTORICAL dependency model used by the backtest (read-only; works on file contents taken from git, never from the working tree).

Soundness stance: every uncertainty adds an edge (the graph over-approximates). It removes an edge only when C# name resolution itself excludes it:
 - a type is visible from a file only if its namespace is in scope there (same or parent namespace, a using directive, a global using, an implicit using) or the use is qualified (then it is kept);
 - a nested type is visible only through its enclosing type (the enclosing type must be mentioned in the file).
Comments and string/char literals are stripped first so braces and words inside them cannot create or hide structure.
"""
import collections, re

TOKEN = re.compile(r'\b[A-Za-z_]\w*\b')
NAMESPACE = re.compile(r'\bnamespace\s+([\w.]+)\s*[;{]')
USING = re.compile(r'^\s*(global\s+)?using\s+(?:static\s+)?(?:\w+\s*=\s*)?([\w.]+)\s*;', re.M)
TYPE_DECL = r'\b(?:class|record\s+struct|record\s+class|record|struct|interface|enum)\s+([A-Za-z_]\w*)'
IMPLICIT = {'System', 'System.Collections.Generic', 'System.IO', 'System.Linq', 'System.Net.Http', 'System.Threading', 'System.Threading.Tasks',
            'Microsoft.AspNetCore.Builder', 'Microsoft.AspNetCore.Hosting', 'Microsoft.AspNetCore.Http', 'Microsoft.AspNetCore.Routing',
            'Microsoft.Extensions.Configuration', 'Microsoft.Extensions.DependencyInjection', 'Microsoft.Extensions.Hosting', 'Microsoft.Extensions.Logging'}


def strip_code(src):
    out = []
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        two = src[i:i + 2]
        if two == '//':
            j = src.find('\n', i)
            i = n if j < 0 else j
        elif two == '/*':
            j = src.find('*/', i + 2)
            i = n if j < 0 else j + 2
        elif re.match(r'[$@]*"""', src[i:i + 5]):
            q = src.index('"""', i)
            k = q
            while k < n and src[k] == '"':
                k += 1
            j = src.find('"' * (k - q), k)
            i = n if j < 0 else j + (k - q)
        elif c == '"' or (c in '$@' and re.match(r'[$@]+"', src[i:i + 4])):
            m = re.match(r'[$@]*"', src[i:])
            verbatim = '@' in m.group(0)
            j = i + len(m.group(0))
            while j < n:
                if verbatim:
                    if src[j] == '"':
                        if src[j + 1:j + 2] == '"':
                            j += 2
                            continue
                        break
                elif src[j] == '\\':
                    j += 2
                    continue
                elif src[j] == '"':
                    break
                j += 1
            i = j + 1
        elif c == "'":
            j = i + 1
            while j < n and src[j] != "'":
                j += 2 if src[j] == '\\' else 1
            i = j + 1
        else:
            out.append(c)
            i += 1
    return ''.join(out)


def read_file(code):
    """code is already stripped. -> (namespace, usings, [(type name, enclosing type name or None)])"""
    m = NAMESPACE.search(code)
    ns = m.group(1) if m else ''
    usings = {u.group(2) for u in USING.finditer(code)}
    types = []
    stack = []                                   # (type name, brace depth at which its body opens)
    depth = 0
    for tok in re.finditer(r'\{|\}|' + TYPE_DECL, code):
        t = tok.group(0)
        if t == '{':
            depth += 1
        elif t == '}':
            depth -= 1
            while stack and stack[-1][1] > depth:
                stack.pop()
        else:
            name = tok.group(1)
            types.append((name, stack[-1][0] if stack else None))
            stack.append((name, depth + 1))
    return ns, usings, types


def scope_of(ns, usings, global_usings):
    sc = set(usings) | set(global_usings) | IMPLICIT
    parts = ns.split('.') if ns else []
    for k in range(1, len(parts) + 1):
        sc.add('.'.join(parts[:k]))
    return sc


def build(files, read, tests_prefix):
    """files: list of paths; read(path) -> source text. -> dict(files, defs, edges, classes, closure)."""
    info = {}
    global_usings = set()
    for f in files:
        code = strip_code(read(f))
        ns, usings, types = read_file(code)
        info[f] = (ns, usings, types, code)
        for u in USING.finditer(code):
            if u.group(1):
                global_usings.add(u.group(2))
    defs = collections.defaultdict(list)
    for f in files:
        ns, _, types, _ = info[f]
        for name, enc in types:
            defs[name].append((f, ns, enc))
    edges = {}
    for f in files:
        ns, usings, types, code = info[f]
        scope = scope_of(ns, usings, global_usings)
        toks = set(TOKEN.findall(code))
        out = set()
        for t in toks:
            for (g, gns, enc) in defs.get(t, ()):
                if g == f:
                    continue
                if enc is not None and enc not in toks:
                    continue
                if (not gns) or gns in scope or re.search(r'\w\.' + re.escape(t) + r'\b', code):
                    out.add(g)
        edges[f] = out
    classes = collections.defaultdict(set)
    for f in files:
        if f.startswith(tests_prefix):
            for name, enc in info[f][2]:
                if enc is None:
                    classes[name].add(f)
    closure = {}

    def clo(f):
        if f in closure:
            return closure[f]
        seen = {f}
        stack = [f]
        while stack:
            x = stack.pop()
            for y in edges.get(x, ()):
                if y not in seen:
                    seen.add(y)
                    stack.append(y)
        closure[f] = seen
        return seen

    return dict(files=set(files), defs=defs, edges=edges, classes=classes, closure=clo, info=info)
