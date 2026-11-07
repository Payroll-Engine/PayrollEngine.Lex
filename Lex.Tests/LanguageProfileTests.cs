using Xunit;
// ReSharper disable StringLiteralTypo

namespace PayrollEngine.Lex.Tests;

public class LanguageProfileTests
{
    private static Entry Entry(string content, string unit = null) => new()
    {
        Key = "Test.2026",
        Content = content,
        Unit = unit,
        SourceUrls = ["https://example.org/doc"]
    };

    private static SourceDocument Sections(params string[] texts) =>
        new() { State = SourceState.Readable, Sections = texts.Select((x, i) => ($"Page {i + 1}", x)).ToList() };

    private static SourceMatch Verify(string content, string text, string validFrom) =>
        SourceVerifier.Verify(Entry(content), Sections(text), validFrom).Match;

    // numbers --------------------------------------------------------------------

    [Theory]
    [InlineData("Der Beitragssatz beträgt 4,25 Prozent.")]
    [InlineData("Der Beitragssatz beträgt 4,25 vom Hundert.")]
    [InlineData("Der Beitragssatz beträgt 4,25 v. H.")]
    [InlineData("Le taux est de 4,25 pour cent.")]
    [InlineData("El tipo es del 4,25 por ciento.")]
    [InlineData("L'aliquota è pari al 4,25 per cento.")]
    [InlineData("Het percentage bedraagt 4,25 procent.")]
    [InlineData("A taxa é de 4,25 por cento.")]
    public void PercentWord_IsPercentage(string text) =>
        Assert.Equal(1, SourceVerifier.Verify(Entry("0.0425", "rate"), Sections(text)).NumbersFound);

    [Fact]
    public void PercentWord_WithinWord_IsNoPercentage() =>
        Assert.Equal(0, SourceVerifier.Verify(Entry("0.0425", "rate"), Sections("Es gelten 4,25 Prozentpunkte.")).NumbersFound);

    [Theory]
    [InlineData("Umsatz bis 1,5 Mio. Euro", 1500000)]
    [InlineData("Umsatz bis 2 Millionen Euro", 2000000)]
    [InlineData("Bilanzsumme 3 Mrd. Euro", 3000000000)]
    [InlineData("Bilanzsumme 2 Milliarden Euro", 2000000000)]
    [InlineData("un plafond de 3 millions d'euros", 3000000)]
    [InlineData("un plafond de 2 milliards d'euros", 2000000000)]
    [InlineData("un límite de 1,5 millones de euros", 1500000)]
    [InlineData("un limite di 2 milioni di euro", 2000000)]
    [InlineData("een grens van 3 miljoen euro", 3000000)]
    [InlineData("een grens van 2 miljard euro", 2000000000)]
    [InlineData("um limite de 1,5 milhões de euros", 1500000)]
    public void ScaleWord_IsNumber(string text, double expected) =>
        Assert.Contains((decimal)expected, SourceDocument.Numbers(text));

    [Theory]
    [InlineData("die Hälfte des Satzes", 0.5)]
    [InlineData("das Eineinhalbfache: eineinhalb", 1.5)]
    [InlineData("anderthalb Stunden", 1.5)]
    [InlineData("la moitié du taux", 0.5)]
    [InlineData("une fois et demie le taux", 1.5)]
    [InlineData("la mitad del tipo", 0.5)]
    [InlineData("una vez y media el tipo", 1.5)]
    [InlineData("la metà dell'aliquota", 0.5)]
    [InlineData("una volta e mezza la retribuzione", 1.5)]
    [InlineData("de helft van het tarief", 0.5)]
    [InlineData("anderhalf keer het loon", 1.5)]
    [InlineData("metade da taxa", 0.5)]
    [InlineData("uma vez e meia o salário", 1.5)]
    public void FractionWord_IsNumber(string text, double expected) =>
        Assert.Contains((decimal)expected, SourceDocument.Numbers(text));

    // year groups ----------------------------------------------------------------

    [Theory]
    [InlineData("Sätze 2024 und 2025", new[] { 2024, 2025 })]
    [InlineData("Sätze 2024 oder 2025", new[] { 2024, 2025 })]
    [InlineData("Sätze 2024 bis 2026", new[] { 2024, 2025, 2026 })]
    [InlineData("taux 2024 et 2025", new[] { 2024, 2025 })]
    [InlineData("taux 2024 ou 2025", new[] { 2024, 2025 })]
    [InlineData("taux de 2024 à 2026", new[] { 2024, 2025, 2026 })]
    [InlineData("du 2024 au 2026", new[] { 2024, 2025, 2026 })]
    [InlineData("tipos 2024 y 2025", new[] { 2024, 2025 })]
    [InlineData("tipos 2024 o 2025", new[] { 2024, 2025 })]
    [InlineData("tipos de 2024 hasta 2026", new[] { 2024, 2025, 2026 })]
    [InlineData("aliquote 2024 e 2025", new[] { 2024, 2025 })]
    [InlineData("aliquote dal 2024 al 2026", new[] { 2024, 2025, 2026 })]
    [InlineData("tarieven 2024 en 2025", new[] { 2024, 2025 })]
    [InlineData("tarieven 2024 of 2025", new[] { 2024, 2025 })]
    [InlineData("tarieven 2024 t/m 2026", new[] { 2024, 2025, 2026 })]
    [InlineData("taxas 2024 ou 2025", new[] { 2024, 2025 })]
    [InlineData("taxas de 2024 até 2026", new[] { 2024, 2025, 2026 })]
    public void YearGroup_Languages(string text, int[] expected) =>
        Assert.Equal(expected, Assert.Single(SourceVerifier.YearHits(text)).Years);

    [Theory]
    [InlineData("Betrag für 2025 und 2026: 8.600 Euro", SourceMatch.Found)]
    [InlineData("Betrag für 2024 und 2025: 8.600 Euro", SourceMatch.Ambiguous)]
    [InlineData("Betrag von 2024 bis 2026: 8.600 Euro", SourceMatch.Found)]
    [InlineData("Montant pour 2024 et 2025 : 8 600 euros", SourceMatch.Ambiguous)]
    [InlineData("Montant de 2025 à 2027 : 8 600 euros", SourceMatch.Found)]
    [InlineData("Importe para 2026 y 2027: 8.600 euros", SourceMatch.Found)]
    [InlineData("Bedrag voor 2023 tot 2025: 8.600 euro", SourceMatch.Ambiguous)]
    public void YearGroup_RatesHit(string text, SourceMatch expected) =>
        Assert.Equal(expected, Verify("8600", text, "2026-01-01"));

    // validity -------------------------------------------------------------------

    [Theory]
    [InlineData("Ab dem 1. Januar {0} beträgt der Betrag 8.600 Euro.")]
    [InlineData("Mit Wirkung vom 1. Jänner {0} beträgt der Betrag 8.600 Euro.")]
    [InlineData("À compter du 1er janvier {0}, le montant est de 8 600 euros.")]
    [InlineData("A partir del 1 de enero de {0}, el importe es de 8.600 euros.")]
    [InlineData("A decorrere dal 1° gennaio {0} l'importo è di 8.600 euro.")]
    [InlineData("Met ingang van 1 januari {0} bedraagt het bedrag 8.600 euro.")]
    [InlineData("Per 1 januari {0} bedraagt het bedrag 8.600 euro.")]
    [InlineData("A partir de 1 de janeiro de {0}, o montante é de 8.600 euros.")]
    [InlineData("A partir de 1º de janeiro de {0}, o montante é de 8.600 euros.")]
    public void Validity_Languages(string text)
    {
        Assert.Equal(SourceMatch.Found, Verify("8600", string.Format(text, 2025), "2026-01-01"));
        Assert.Equal(SourceMatch.Ambiguous, Verify("8600", string.Format(text, 2027), "2026-01-01"));
    }

    [Theory]
    [InlineData("Die Sätze gelten für 2025 und folgende: 8.600 Euro.")]
    [InlineData("Les taux s'appliquent à 2025 et suivantes : 8 600 euros.")]
    [InlineData("Los tipos se aplican a 2025 y siguientes: 8.600 euros.")]
    [InlineData("Le aliquote si applicano al 2025 e successivi: 8.600 euro.")]
    [InlineData("De tarieven gelden voor 2025 en later: 8.600 euro.")]
    [InlineData("As taxas aplicam-se a 2025 e seguintes: 8.600 euros.")]
    public void ValidityAfter_Languages(string text) =>
        Assert.Equal(SourceMatch.Found, Verify("8600", text, "2026-01-01"));

    [Theory]
    [InlineData("Der Betrag von 8.600 Euro ist unverändert gegenüber 2025.")]
    [InlineData("Der Betrag von 8.600 Euro ist unverändert seit 2025.")]
    [InlineData("Le montant de 8 600 euros est inchangé par rapport à 2025.")]
    [InlineData("El importe de 8.600 euros sigue sin cambios respecto a 2025.")]
    [InlineData("L'importo di 8.600 euro è invariato rispetto al 2025.")]
    [InlineData("Het bedrag van 8.600 euro is ongewijzigd ten opzichte van 2025.")]
    [InlineData("O montante de 8.600 euros está inalterado desde 2025.")]
    public void Unchanged_Languages(string text) =>
        Assert.Equal(SourceMatch.Found, Verify("8600", text, "2026-01-01"));

    [Fact]
    public void Validity_IsNoPositiveEvidence() =>
        Assert.Equal(SourceMatch.Ambiguous,
            SourceVerifier.Verify(Entry("8600"),
                Sections("Ab dem 1. Januar 2026 beträgt der Betrag 8.600 Euro.", "Betrag 2025: 8.600 Euro."),
                "2026-01-01").Match);

    // enactments -------------------------------------------------------------------

    [Theory]
    [InlineData("Gesetz vom 20. Dezember 2024")]
    [InlineData("Verordnung vom 3. März 2024")]
    [InlineData("loi du 23 décembre 2024")]
    [InlineData("décret du 1er août 2024")]
    [InlineData("Ley 7/2024, de 20 de diciembre de 2024")]
    [InlineData("Orden de 15 de marzo de 2024")]
    [InlineData("legge 30 dicembre 2024")]
    [InlineData("decreto del 15 marzo 2024")]
    [InlineData("besluit van 12 juli 2024")]
    [InlineData("Portaria de 3 de abril de 2024")]
    public void EnactmentDate_IsNoYear(string text) =>
        Assert.Empty(SourceVerifier.Years(text));

    // collisions -------------------------------------------------------------------

    [Theory]
    [InlineData("Abzug 2024: 8.600 Euro")]
    [InlineData("Super 2024: 8,600")]
    [InlineData("Medal 2024: 8,600")]
    [InlineData("Per capita, see page 3. In 2024 the amount was 8,600.")]
    [InlineData("Ab Seite 3. Im Jahr 2024 betrug der Betrag 8.600 Euro.")]
    [InlineData("In 2024 a deduction of 8,600 applied.")]
    public void ShortWord_OutsideItsPosition_HasNoEffect(string text) =>
        Assert.Equal(SourceMatch.Ambiguous, Verify("8600", text, "2025-01-01"));

    [Theory]
    [InlineData("in 2023 e poi nel 2025")]
    [InlineData("in 2023 a total, in 2025")]
    [InlineData("2023 of the 2025 rates")]
    public void ShortWord_NotBetweenTwoYears_IsNoGroup(string text) =>
        Assert.Equal(2, SourceVerifier.YearHits(text).Count);

    [Fact]
    public void Alternation_LongestFirst() =>
        Assert.Equal(@"a\s+partir\s+del|a\s+partir\s+de|ab",
            LanguageProfile.Alternation(["ab", "a partir de", "a partir del", "AB"]));
}
