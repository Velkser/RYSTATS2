# Strucna historia chatu

## 1) Co bolo poziadovane a co bolo dodane

1. Napojit a overit AI chat s OpenAI + Ryanair.
Odpoved: integracia fungovala, API sa overilo cez testovacie poziadavky a odpovede prisli v spravnych jazykoch.

2. Odstranit blokovanie kvoli `maxDistance` a opravit logiku vyberu destinacii.
Odpoved: `maxDistance` uz nie je povinne pole a doplnilo sa presne filtrovanie podla cieloveho mesta.

3. Opravit situaciu "nenaslo ziadne cesty" pri konkretnom meste (napr. Pariz).
Odpoved: bol pridany fallback s alternativami (blizke huby) a oznacenie `isAlternative`.

4. Odstranit hardcodovane prve spravy v chate.
Odpoved: uvodne testovacie spravy boli odstranene, chat startuje cisto.

5. Urobit destination nepovinne (discovery/random rezim).
Odpoved: destination je volitelne a agent vie navrhovat moznosti aj bez urceneho mesta.

6. Rozsirit budget vyhladavanie, aby bolo menej prazdnych vysledkov.
Odpoved: interny budget envelope sa zvacsil (`+120` alebo `x2`) na lepsie pokrytie relevantnych letov.

7. Pridat kratku dokumentaciu k AI agentovi a spravit commit.
Odpoved: dokumentacia bola vytvorena a commit je lokalne ulozeny.

8. Push do GitHubu.
Odpoved: push zlyhal kvoli pravom/uctu (`403`), branch zostal lokalne ahead o 1 commit.
