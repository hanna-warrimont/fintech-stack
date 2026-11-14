// Budget-Alarm 20 € pro Monat auf die Ressourcengruppe. Ein Budget ALARMIERT nur – es stoppt keine Ressource.
// Der Schutz vor Kosten ist der Abriss (destroy.sh); der Alarm ist das Netz darunter.
param budgetEmail string
param startDate string

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: 'portal-monthly-20'
  properties: {
    category: 'Cost'
    amount: 20
    timeGrain: 'Monthly'
    timePeriod: { startDate: startDate }
    notifications: {
      Actual_GreaterThan_80: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: [ budgetEmail ]
      }
      Forecasted_GreaterThan_100: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [ budgetEmail ]
      }
    }
  }
}
