using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Newtonsoft.Json;
using Task = System.Threading.Tasks.Task;

namespace SixB.Hackathon;

public class IntakeOuttakeService
{
    private ResourceReference _personalWardTeam =
        new ResourceReference($"CareTeam/1add538f-ae39-4176-88ec-e37b35b9a6fa", "6B Virtual Ward Team");
    
    private ResourceReference _managingOrg = new ResourceReference("Organization/0920c700-f51e-40cc-a297-dfff43801dd5", "North West Ambulance Trust");
    private string _managingOdsCode = "RX7";

    private ResourceReference _doctorRef = new ResourceReference($"1bdd198b-4ce1-4aa8-a1ee-12c922d30a6c")
    {
        Identifier = new Identifier("https://fhir.hl7.org.uk/Id/gmp-number","G0109459")
    };

    private FhirClient _client;

    public IntakeOuttakeService()
    {
        _client = new FhirClient("https://3cdzg7kbj4.execute-api.eu-west-2.amazonaws.com/poc/events/FHIR/R4/");
    }
    
    public async Task<string> AdmitPatientToVirtualWard(string nhsNumber)
    {
        var patient = new ResourceReference()
        {
            Identifier = new Identifier("https://fhir.nhs.uk/Id/nhs-number", nhsNumber)
        };
        var start = GenerateStartOfEpisode(patient, "RX7"); //Hardcoded for POC
        var admit = GenerateAdmitEncounter(_doctorRef, patient, new ResourceReference($"urn:uuid:{start.Id}"), _personalWardTeam);

        var transactionBundle = new Bundle
        {
            Type = Bundle.BundleType.Transaction
        };
        transactionBundle.Entry.Add(new Bundle.EntryComponent
        {
            Resource = start,
            Request = new Bundle.RequestComponent
            {
                Url = "EpisodeOfCare",
                Method = Bundle.HTTPVerb.POST
            }
        });
        transactionBundle.Entry.Add(new Bundle.EntryComponent
        {
            Resource = admit,
            Request = new Bundle.RequestComponent
            {
                Url = "Encounter",
                Method = Bundle.HTTPVerb.POST
            }
        });
        var res = await _client.TransactionAsync(transactionBundle);
        if(res == null) throw new NullReferenceException("Failed to create episode of care");
        Console.WriteLine(JsonConvert.SerializeObject(res));
        var episodeOfCare = res.Entry.FirstOrDefault(x => x.Resource is EpisodeOfCare)?.Resource as EpisodeOfCare;
        if (episodeOfCare == null) throw new NullReferenceException("Failed to create episode of care");
        return episodeOfCare.Id;
    }
    public async Task DischargePatient(string nhsNumber, string episodeOfCareIdentifier)
    {
        var patientIdentifier = new ResourceReference
        {
            Identifier = new Identifier
            {
                System = "https://fhir.nhs.uk/Id/nhs-number",
                Value = nhsNumber
            }
        };
        var transactionBundle = new Bundle
        {
            Type = Bundle.BundleType.Transaction
        };
        var endOfEpisode = await GetAndEndEpisodeOfCare(episodeOfCareIdentifier);
        if (endOfEpisode == null) throw new NullReferenceException("Failed to find end of episode");
        var discharge = GenerateDischargeEncounter(_doctorRef, patientIdentifier, new ResourceReference($"urn:uuid:{endOfEpisode.Id}"), _personalWardTeam);
        transactionBundle.Entry.Add(new Bundle.EntryComponent
        {
            Resource = endOfEpisode,
            Request = new Bundle.RequestComponent
            {
                Url = "EpisodeOfCare",
                Method = Bundle.HTTPVerb.POST
            }
        });
        transactionBundle.Entry.Add(new Bundle.EntryComponent
        {
            Resource = discharge,
            Request = new Bundle.RequestComponent
            {
                Url = "Encounter",
                Method = Bundle.HTTPVerb.POST
            }
        });
    }

    /// <summary>
    /// Patient has been admitted to the ward.
    /// </summary>
    /// <param name="patientReference"></param>
    /// <param name="managingOds"></param>
    /// <returns></returns>
    private EpisodeOfCare GenerateStartOfEpisode(ResourceReference patientReference, string managingOds)
    {
        var episodeOfCare = new EpisodeOfCare
        {
            Status = EpisodeOfCare.EpisodeOfCareStatus.Active,
            Patient = patientReference,
            ManagingOrganization = new ResourceReference
            {
                Identifier = new Identifier("https://fhir.nhs.uk/Id/ods-organization-code", managingOds)
            },
            Type = new List<CodeableConcept>
            {
                new("http://terminology.hl7.org/CodeSystem/episodeofcare-type", "hacc", "Home and Community Care",
                    "Virtual Wards")
            },
            StatusHistory = new List<EpisodeOfCare.StatusHistoryComponent>
            {
                new()
                {
                    Status = EpisodeOfCare.EpisodeOfCareStatus.Active,
                    Period = new Period
                    {
                        StartElement = FhirDateTime.Now()
                    }
                }
            },
            Period = new Period
            {
                StartElement = FhirDateTime.Now()
            }
        };
        return episodeOfCare;
    }

    private async Task<EpisodeOfCare?> GetAndEndEpisodeOfCare(string episodeOfCareIdentifier)
    {
        var eoc = await _client.GetAsync($"EpisodeOfCare/{episodeOfCareIdentifier}");
        if (eoc == null || eoc is not Bundle bundle) return null;
        if (!bundle.Any()) return null;
        var episodeOfCare = bundle.Entry.First().Resource as EpisodeOfCare;
        if (episodeOfCare == null) return null;
        episodeOfCare.Status = EpisodeOfCare.EpisodeOfCareStatus.Finished;
        episodeOfCare.Period.EndElement = FhirDateTime.Now();
        return episodeOfCare;
    }

    private Encounter GenerateDischargeEncounter(ResourceReference dischargingClinicianIdentifier,
        ResourceReference patientIdentifier, ResourceReference episodeIdentifier, ResourceReference virtualWardProvider)
    {
        var enc = new Encounter
        {
            Id = Guid.NewGuid().ToString(),
            Subject = patientIdentifier,
            Status = Encounter.EncounterStatus.Finished,
            EpisodeOfCare = new List<ResourceReference>
            {
                episodeIdentifier
            },
            Class = new Coding("http://terminology.hl7.org/CodeSystem/v3-ActCode", "VR", "Virtual"),
            ServiceType = new CodeableConcept("http://snomed.info/sct", "58000006", "Discharge from Virtual Ward",
                "Patient discharge"),
            Hospitalization = new Encounter.HospitalizationComponent
            {
                AdmitSource = new CodeableConcept("https://fhir.hl7.org.uk/CodeSystem/UKCore-SourceOfAdmissionEngland",
                    "99")
            },
            ServiceProvider = virtualWardProvider
        };
        var dischargingDoctor = new Encounter.ParticipantComponent
        {
            Type = //Attending Type
                new List<CodeableConcept>
                {
                    new("http://terminology.hl7.org/CodeSystem/v3-ParticipationType", "ATND", "attender",
                        "Attender")
                },
            Individual = dischargingClinicianIdentifier
        };
        enc.Participant.Add(dischargingDoctor);
        var admissionExt = new Extension("https://fhir.hl7.org.uk/StructureDefinition/Extension-UKCore-AdmissionMethod",
            new CodeableConcept("https://fhir.hl7.org.uk/CodeSystem/UKCore-AdmissionMethodEngland", "99"));
        var dischargeExt = new Extension("https://fhir.hl7.org.uk/StructureDefinition/Extension-UKCore-DischargeMethod",
            new FhirDecimal(1)); //Discharge under clinical advice
        enc.Extension.Add(admissionExt);
        enc.Extension.Add(dischargeExt);
        return enc;
    }

    private Encounter GenerateAdmitEncounter(ResourceReference admittingClinicianIdentifier,
        ResourceReference patientIdentifier, ResourceReference episodeIdentifier, ResourceReference virtualWardProvider)
    {
        var enc = new Encounter
        {
            Id = Guid.NewGuid().ToString(),
            Subject = patientIdentifier,
            Status = Encounter.EncounterStatus.Finished,
            EpisodeOfCare = new List<ResourceReference>
            {
                episodeIdentifier
            },
            Class = new Coding("http://terminology.hl7.org/CodeSystem/v3-ActCode", "VR", "Virtual"),
            ServiceType = new CodeableConcept("http://snomed.info/sct", "894881000000108", "Admitted to Virtual Ward",
                "Admission to observation ward"),
            Hospitalization = new Encounter.HospitalizationComponent
            {
                AdmitSource = new CodeableConcept("https://fhir.hl7.org.uk/CodeSystem/UKCore-SourceOfAdmissionEngland",
                    "99")
            },
            ServiceProvider = virtualWardProvider
        };
        var dischargingDoctor = new Encounter.ParticipantComponent
        {
            Type = //Attending Type
                new List<CodeableConcept>
                {
                    new("http://terminology.hl7.org/CodeSystem/v3-ParticipationType", "ATND", "attender",
                        "Attender")
                },
            Individual = admittingClinicianIdentifier
        };
        enc.Participant.Add(dischargingDoctor);
        var admissionExt = new Extension("https://fhir.hl7.org.uk/StructureDefinition/Extension-UKCore-AdmissionMethod",
            new CodeableConcept("https://fhir.hl7.org.uk/CodeSystem/UKCore-AdmissionMethodEngland", "99"));
        enc.Extension.Add(admissionExt);
        return enc;
    }
}