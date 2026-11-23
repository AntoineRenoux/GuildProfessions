#!/usr/bin/env python3
"""Génère server/wwwroot/docs/*.html à partir des fragments bilingues de src/.

Usage : python3 tools/docs/build.py [dossier de sortie]
(par défaut : server/wwwroot/docs). Modifier les fragments de src/, jamais les
pages générées : elles seraient écrasées à la génération suivante.

Macros dans les fragments :
  [[fr||en]]          -> <span lang="fr">fr</span><span lang="en">en</span>
  {b:Libellé}         -> bouton de l'addon        {t:Onglet} -> onglet de l'addon
  {o:Option}          -> case / champ de l'addon  {k:Maj}    -> touche
  {d:fr||en}          -> commande Discord (nom FR / nom EN)
  {g:/gkeep tuto}     -> commande en jeu
  {who:all|officer|org|admin|self|crafter|requester}
  {tip} {warn} {note} ... {/tip} {/warn} {/note}  -> encadrés
"""
import html, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "..", "server", "wwwroot", "docs")

PAGES = [
    # (fichier, groupe, titre fr, titre en, icône)
    ("index", "start", "Documentation", "Documentation", "i-faq"),
    ("installation", "start", "Installer Guildkeep", "Installing Guildkeep", "i-install"),
    ("compagnon", "start", "L'app compagnon", "The companion app", "i-compagnon"),
    ("tableau", "mod", "Tableau de bord et thèmes", "Dashboard and themes", "i-tableau"),
    ("metiers", "mod", "Métiers", "Professions", "i-metiers"),
    ("membres", "mod", "Membres", "Members", "i-membres"),
    ("artisanat", "mod", "Artisanat", "Crafting orders", "i-artisanat"),
    ("evenements", "mod", "Événements", "Events", "i-evenements"),
    ("soiree-raid", "mod", "Soirée de raid", "Raid night", "i-raid"),
    ("presence", "mod", "Présence et butin", "Attendance and loot", "i-presence"),
    ("quetes", "mod", "Quêtes", "Quests", "i-quetes"),
    ("communication", "mod", "Communication", "Communication", "i-communication"),
    ("profils", "mod", "Profils et accueil", "Profiles and onboarding", "i-profils"),
    ("commandes", "ref", "Commandes Discord", "Discord commands", "i-cmd"),
    ("faq", "ref", "FAQ et dépannage", "FAQ and troubleshooting", "i-faq"),
]
GROUPS = {"start": ("Démarrer", "Getting started"), "mod": ("Modules", "Modules"), "ref": ("Référence", "Reference")}


def url(slug):
    return "/docs/" if slug == "index" else f"/docs/{slug}.html"


def tr(fr, en):
    return f'<span lang="fr">{fr}</span><span lang="en">{en}</span>'


WHO = {
    "all": ("b-all", "Tout membre", "Any member"),
    "officer": ("b-officer", "Officier", "Officer"),
    "org": ("b-org", "Organisateur ou officier", "Organizer or officer"),
    "admin": ("b-admin", "Admin « Gérer le serveur »", "Admin (\u201cManage Server\u201d)"),
    "self": ("b-self", "Toi seul", "Only you"),
    "crafter": ("b-org", "L'artisan", "The crafter"),
    "requester": ("b-self", "Le demandeur", "The requester"),
}
CALLOUTS = {"tip": ("Astuce", "Tip"), "warn": ("Attention", "Watch out"), "note": ("Bon à savoir", "Good to know")}


def expand(text):
    def pair(s):
        if "||" in s:
            fr, en = s.split("||", 1)
            return tr(fr, en)
        return s

    # Libellé de l'addon (interface en français) : en anglais, le libellé du jeu suivi de sa traduction.
    def game(cls, body):
        if "||" not in body:
            return f'<span class="{cls}">{body}</span>'
        fr, en = body.split("||", 1)
        if " — " in en:
            en = en.split(" — ", 1)[1]
        if en == fr:
            return f'<span class="{cls}">{fr}</span>'
        return tr(f'<span class="{cls}">{fr}</span>', f'<span class="{cls}">{fr}</span> <span class="gloss">({en})</span>')

    # {B:…} : bouton du site ou du bot Discord, traduit lui-même.
    text = re.sub(r"\{B:([^{}]+)\}", lambda m: f'<span class="ui">{pair(m.group(1))}</span>', text)
    text = re.sub(r"\{b:([^{}]+)\}", lambda m: game("ui", m.group(1)), text)
    text = re.sub(r"\{t:([^{}]+)\}", lambda m: game("ui tab", m.group(1)), text)
    text = re.sub(r"\{o:([^{}]+)\}", lambda m: game("ui opt", m.group(1)), text)
    text = re.sub(r"\{k:([^{}]+)\}", lambda m: f'<kbd class="key">{m.group(1)}</kbd>', text)
    text = re.sub(r"\{d:([^{}]+)\}", lambda m: f'<code class="d">{pair(m.group(1))}</code>', text)
    text = re.sub(r"\{g:([^{}]+)\}", lambda m: f'<code>{m.group(1)}</code>', text)
    text = re.sub(r"\{who:(\w+)\}", lambda m: '<span class="badge {0}">{1}</span>'.format(WHO[m.group(1)][0], tr(*WHO[m.group(1)][1:])), text)
    for key, (fr, en) in CALLOUTS.items():
        text = text.replace("{" + key + "}", f'<aside class="callout c-{key}"><p class="callout-title">{tr(fr, en)}</p>')
        text = text.replace("{/" + key + "}", "</aside>")
    # Paires imbriquées : la plus intérieure d'abord (aucun « [[ » à l'intérieur).
    inner = re.compile(r"\[\[((?:(?!\[\[).)+?)\|\|((?:(?!\[\[).)+?)\]\]", re.S)
    while True:
        text, n = inner.subn(lambda m: tr(m.group(1), m.group(2)), text)
        if not n:
            break
    return text


ICONS = """<svg class="sprite" aria-hidden="true" focusable="false"><defs><linearGradient id="gk-gold" gradientUnits="userSpaceOnUse" x1="0" y1="4" x2="0" y2="44"><stop offset="0" stop-color="#fff1bf"/><stop offset=".45" stop-color="#f0c25e"/><stop offset="1" stop-color="#9a6a20"/></linearGradient>
<symbol id="i-metiers" viewBox="0 0 48 48"><path fill="url(#gk-gold)" d="M6 15h25c1 5 5 8 11 8v4H31l3 9H14l3-9h-3C9 27 6 22 6 15z"/><path fill="none" stroke="url(#gk-gold)" stroke-linecap="round" stroke-width="3.2" d="M36 6l4 4M33 9l4 4"/></symbol>
<symbol id="i-membres" viewBox="0 0 48 48"><path fill="url(#gk-gold)" d="M24 5c9 0 15 7 15 16v14l-6 6h-4V27h-10v14h-4l-6-6V21C9 12 15 5 24 5z"/><path fill="#1a1208" d="M15 20h18v4H15z"/></symbol>
<symbol id="i-artisanat" viewBox="0 0 48 48"><path fill="none" stroke="url(#gk-gold)" stroke-linecap="round" stroke-width="3.2" d="M10 38L36 12"/><ellipse fill="none" stroke="url(#gk-gold)" stroke-width="3.2" cx="37.5" cy="10.5" rx="3.2" ry="1.8" transform="rotate(-45 37.5 10.5)"/><path fill="none" stroke="url(#gk-gold)" stroke-linecap="round" stroke-width="1.8" d="M39 9c6 6-4 12-10 16s-14 10-8 17"/></symbol>
<symbol id="i-evenements" viewBox="0 0 48 48"><path fill="url(#gk-gold)" d="M7 11h34v30H7z"/><path fill="#1a1208" d="M10 19h28v19H10z"/><path fill="url(#gk-gold)" d="M13 22h6v5h-6zM21 22h6v5h-6zM29 22h6v5h-6zM13 30h6v5h-6zM21 30h6v5h-6z"/><path fill="none" stroke="url(#gk-gold)" stroke-linecap="round" stroke-width="3.2" d="M15 6v8M33 6v8"/></symbol>
<symbol id="i-raid" viewBox="0 0 48 48"><path fill="url(#gk-gold)" d="M24 4l17 6v12c0 11-7 18-17 22C14 40 7 33 7 22V10z"/><path fill="#1a1208" d="M24 11l11 4v8c0 7-4 12-11 15z"/></symbol>
<symbol id="i-presence" viewBox="0 0 48 48"><path fill="url(#gk-gold)" d="M10 6h22l7 7v29H10z"/><path fill="none" stroke="#1a1208" stroke-linecap="round" stroke-linejoin="round" stroke-width="3.4" d="M16 26l5 5 10-11"/></symbol>
<symbol id="i-quetes" viewBox="0 0 48 48"><path fill="url(#gk-gold)" d="M20 5h8l-1.5 26h-5zM24 35a4 4 0 1 1 0 8 4 4 0 0 1 0-8z"/></symbol>
<symbol id="i-communication" viewBox="0 0 48 48"><path fill="url(#gk-gold)" d="M6 19h8l20-11v32L14 29H6z"/><path fill="none" stroke="url(#gk-gold)" stroke-linecap="round" stroke-width="3.2" d="M39 17c3 4 3 10 0 14M12 30l3 11h6l-3-11"/></symbol>
<symbol id="i-profils" viewBox="0 0 48 48"><path fill="url(#gk-gold)" d="M5 10h38v28H5z"/><circle fill="#1a1208" cx="16" cy="21" r="5"/><path fill="#1a1208" d="M8 33c1-5 4-7 8-7s7 2 8 7zM27 17h12v3H27zM27 24h12v3H27zM27 31h8v3h-8z"/></symbol>
<symbol id="i-tableau" viewBox="0 0 48 48"><path fill="url(#gk-gold)" d="M6 8h16v14H6zM26 8h16v22H26zM6 26h16v14H6zM26 34h16v6H26z"/></symbol>
<symbol id="i-install" viewBox="0 0 48 48"><path fill="url(#gk-gold)" d="M17 12c0-5 3-8 7-8s7 3 7 8h-3c0-3-2-5-4-5s-4 2-4 5z"/><path fill="url(#gk-gold)" d="M10 14h28l4 26a4 4 0 0 1-4 4H10a4 4 0 0 1-4-4z"/><path fill="#1a1208" d="M20 22h8v6h-8z"/></symbol>
<symbol id="i-compagnon" viewBox="0 0 48 48"><path fill="none" stroke="url(#gk-gold)" stroke-linecap="round" stroke-width="4" d="M6 34c6-14 30-14 36 0M12 34V22M24 34V18M36 34V22"/><path fill="url(#gk-gold)" d="M4 34h40v5H4z"/></symbol>
<symbol id="i-cmd" viewBox="0 0 48 48"><path fill="url(#gk-gold)" d="M5 9h38v30H5z"/><path fill="#1a1208" d="M8 15h32v21H8z"/><path fill="none" stroke="url(#gk-gold)" stroke-linecap="round" stroke-width="3.2" d="M14 32l8-13M26 32h8"/></symbol>
<symbol id="i-faq" viewBox="0 0 48 48"><path fill="url(#gk-gold)" d="M8 6h26l6 6v30H8z"/><path fill="none" stroke="#1a1208" stroke-linecap="round" stroke-width="3.4" d="M19 19c0-4 3-6 5.5-6s5.5 2 5.5 5.5-5 4-5 8M24.5 32v.5"/></symbol>
</defs></svg>"""


def head(title, description):
    return f"""<!doctype html>
<html lang="fr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{html.escape(title)}</title>
<meta name="description" content="{html.escape(description)}">
<link rel="preload" href="/fonts/cinzel-latin.woff2" as="font" type="font/woff2" crossorigin>
<link rel="preload" href="/fonts/alegreya-sans-400.woff2" as="font" type="font/woff2" crossorigin>
<link rel="stylesheet" href="/site.css?v=6">
<script>document.documentElement.classList.add('js');try{{var l=localStorage.getItem('gk-lang')||((navigator.language||'fr').toLowerCase().indexOf('fr')===0?'fr':'en');document.documentElement.lang=l}}catch(e){{}}</script>
</head>
<body>
"""


TOPBAR = """<header class="topbar">
	<a class="wordmark" href="/">Guildkeep</a>
	<nav aria-label="Navigation">
		<a href="/docs/" aria-current="page"><span lang="fr">Documentation</span><span lang="en">Docs</span></a>
		<a class="hide-phone" href="/docs/faq.html"><span lang="fr">Aide</span><span lang="en">Help</span></a>
		<div class="lang-switch" role="group" aria-label="Langue / Language">
			<button type="button" data-set-lang="fr" aria-pressed="true">FR</button>
			<button type="button" data-set-lang="en" aria-pressed="false">EN</button>
		</div>
		<a class="button invite-link hide-phone" href="/#mise-en-place"><span lang="fr">Ajouter à Discord</span><span lang="en">Add to Discord</span></a>
	</nav>
</header>
"""

FOOTER = """<footer class="foot">
	<span><span lang="fr">Outil de guilde pour WoW: Forever</span><span lang="en">Guild tool for WoW: Forever</span></span>
	<nav aria-label="Pied de page / Footer">
		<a href="/"><span lang="fr">Accueil</span><span lang="en">Home</span></a>
		<a href="/docs/"><span lang="fr">Documentation</span><span lang="en">Documentation</span></a>
		<a href="/confidentialite.html"><span lang="fr">Confidentialité</span><span lang="en">Privacy</span></a>
		<a href="/conditions.html"><span lang="fr">Conditions</span><span lang="en">Terms</span></a>
		<a href="https://github.com/AntoineRenoux/GuildProfessions" rel="noopener"><span lang="fr">Code source</span><span lang="en">Source code</span></a>
	</nav>
	<span class="end">2026</span>
	<span class="credit"><span lang="fr">World of Warcraft est une marque de Blizzard Entertainment ; Guildkeep n'y est pas affilié.</span><span lang="en">World of Warcraft is a trademark of Blizzard Entertainment; Guildkeep is not affiliated.</span></span>
</footer>

<script src="/site.js?v=6" defer></script>
</body>
</html>
"""


def read_src(slug):
    raw = open(os.path.join(HERE, "src", slug + ".html"), encoding="utf-8").read()
    meta, body = raw.split("\n---\n", 1)
    info = {}
    for line in meta.strip().splitlines():
        k, v = line.split(":", 1)
        info[k.strip()] = v.strip()
    return info, body


def sections_of(body):
    return re.findall(r'<h2 id="([^"]+)">(.*?)</h2>', body, flags=re.S)


def nav(current, current_sections):
    out = ['<aside class="doc-nav" aria-label="Sommaire / Contents"><details open><summary><span><span lang="fr">Sommaire</span><span lang="en">Contents</span></span><i class="plus" aria-hidden="true"></i></summary><div>']
    for gkey, (gfr, gen) in GROUPS.items():
        out.append(f'<p class="doc-nav-label">{tr(gfr, gen)}</p><ul>')
        for slug, g, fr, en, _ in PAGES:
            if g != gkey:
                continue
            here = ' aria-current="page"' if slug == current else ""
            out.append(f'<li><a href="{url(slug)}"{here}>{tr(fr, en)}</a>')
            if slug == current and current_sections:
                out.append("<ul>")
                for sid, label in current_sections:
                    clean = re.sub(r'<a class="anchor".*?</a>', "", label)
                    out.append(f'<li><a href="#{sid}">{clean}</a></li>')
                out.append("</ul>")
            out.append("</li>")
        out.append("</ul>")
    out.append("</div></details></aside>")
    return "\n".join(out)


def pager(index):
    parts = ['<nav class="doc-pager" aria-label="Pages voisines / Adjacent pages">']
    if index > 0:
        s, _, fr, en, _ = PAGES[index - 1]
        parts.append(f'<a class="prev" href="{url(s)}" rel="prev"><small>{tr("← Précédent", "← Previous")}</small><b>{tr(fr, en)}</b></a>')
    if index < len(PAGES) - 1:
        s, _, fr, en, _ = PAGES[index + 1]
        parts.append(f'<a class="next" href="{url(s)}" rel="next"><small>{tr("Suivant →", "Next →")}</small><b>{tr(fr, en)}</b></a>')
    parts.append("</nav>")
    return "\n".join(parts)


def build():
    os.makedirs(OUT, exist_ok=True)
    for i, (slug, _, fr, en, _) in enumerate(PAGES):
        info, body = read_src(slug)
        body = expand(body)
        # Ancre cliquable sur chaque titre de section.
        body = re.sub(r'<h2 id="([^"]+)">', lambda m: f'<h2 id="{m.group(1)}"><a class="anchor" href="#{m.group(1)}" aria-hidden="true" tabindex="-1">#</a>', body)
        secs = sections_of(body)
        crumbs = [f'<li><a href="/">{tr("Accueil", "Home")}</a></li>']
        if slug != "index":
            crumbs.append(f'<li><a href="/docs/">{tr("Documentation", "Documentation")}</a></li>')
        crumbs.append(f'<li aria-current="page">{tr(fr, en)}</li>')
        lead = expand(info.get("lead", ""))
        page = (
            head(f"Guildkeep · {info.get('title', fr)}", info.get("description", ""))
            + ICONS + "\n" + TOPBAR
            + '\n<div class="doc">\n'
            + nav(slug, secs)
            + '\n<main class="doc-main" id="contenu">\n'
            + f'<nav class="crumbs" aria-label="Fil d\'Ariane / Breadcrumb"><ol class="crumbs-list">{"".join(crumbs)}</ol></nav>\n'
            + f'<h1>{tr(info.get("h1fr", fr), info.get("h1en", en))}</h1>\n'
            + (f'<p class="lead">{lead}</p>\n' if lead else "")
            + '<div class="rule" aria-hidden="true"><i></i></div>\n'
            + body.strip() + "\n"
            + pager(i)
            + "\n</main>\n</div>\n\n"
            + FOOTER
        )
        name = "index.html" if slug == "index" else f"{slug}.html"
        with open(os.path.join(OUT, name), "w", encoding="utf-8") as f:
            f.write(page)
        leftovers = re.findall(r"\[\[|\]\]|\{(?:b|t|o|d|g|k|who):|\{/?(?:tip|warn|note)\}", page)
        print(f"{name}: {len(page)//1024} Ko, {len(secs)} sections" + (f"  !! macros non résolues : {leftovers[:5]}" if leftovers else ""))


build()
