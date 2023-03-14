using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Newtonsoft.Json;
using Task = System.Threading.Tasks.Task;

namespace SixB.Hackathon;

public class ObservationService
{
    public FhirClient Client { get; set; }

    public ObservationService()
    {
        Client = new FhirClient("https://3cdzg7kbj4.execute-api.eu-west-2.amazonaws.com/poc/events/FHIR/R4/");
    }

    public async System.Threading.Tasks.Task CreateObservation(string odsCode, string clinicianIdentifier,
        string patientIdentifier, string patientNhsNumber, decimal news2Score)
    {
        Console.WriteLine("Creating Observation");
        var obs = GenerateBaseObservation(odsCode, false, clinicianIdentifier, patientIdentifier, patientNhsNumber);
        obs.SetNews2Scores(news2Score);
        var bpObs = GenerateBaseObservation(odsCode, false, clinicianIdentifier, patientIdentifier, patientNhsNumber);
        bpObs.SetBloodPressureValues(0, 0);
        var hpObs = GenerateBaseObservation(odsCode, true, clinicianIdentifier, patientIdentifier, patientNhsNumber);
        hpObs.SetHeartRateValues(0);
        Console.WriteLine("Scores set");
        var sendBundle = obs.ToTransactionBundle();
        sendBundle.Entry.Add(new Bundle.EntryComponent
        {
            FullUrl = $"urn:uuid:{hpObs.Id}",
            Request = new Bundle.RequestComponent
            {
                Url = "Observation",
                Method = Bundle.HTTPVerb.POST
            },
            Resource = hpObs
        });
        sendBundle.Entry.Add(new Bundle.EntryComponent
        {
            FullUrl = $"urn:uuid:{bpObs.Id}",
            Request = new Bundle.RequestComponent
            {
                Url = "Observation",
                Method = Bundle.HTTPVerb.POST
            },
            Resource = bpObs
        });
        Console.WriteLine("FhirBundle");
        Console.WriteLine(JsonConvert.SerializeObject(sendBundle));
        try
        {
            var res = await Client.TransactionAsync(sendBundle);
            Console.WriteLine(JsonConvert.SerializeObject(res));
        }
        catch (FhirOperationException e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private Observation GenerateBaseObservation(string odsCode, bool isSelfObs, string clinicianIdentifier = "",
        string patientIdentifier = "", string patientNhsNumber = "")
    {
        ResourceReference subject;
        if (!string.IsNullOrWhiteSpace(patientNhsNumber))
        {
            subject = new ResourceReference()
            {
                Identifier = new Identifier("https://fhir.nhs.uk/Id/nhs-number", patientNhsNumber)
            };
        }
        else
        {
            subject = new ResourceReference($"Patient/{patientIdentifier}");
        }

        var performer = new List<ResourceReference>();
        if (isSelfObs)
        {
            performer.Add(subject);
        }
        else
        {
            performer.AddRange(new[]
            {
                new ResourceReference
                {
                    Identifier = new Identifier("https://fhir.hl7.org.uk/Id/gmc-number", clinicianIdentifier)
                },
                new ResourceReference
                {
                    Identifier = new Identifier("https://fhir.nhs.uk/Id/ods-organization-code", odsCode)
                }
            });
        }

        var obs = new Observation
        {
            Id = Guid.NewGuid().ToString(),
            Status = ObservationStatus.Final,
            Category = new List<CodeableConcept>
            {
                new("https://terminology.hl7.org/CodeSystem/observation-category", "survey", "Survey")
            },

            Effective = FhirDateTime.Now(),
            Performer = performer
        };
        return obs;
    }
}

public static class ObservationExtensions
{
    /// <summary>
    /// Replaces any existing code and value on the observation with a NEWS2 score and value as given by the user.
    /// </summary>
    /// <param name="obs"></param>
    /// <param name="value"></param>
    public static void SetNews2Scores(this Observation obs, decimal value)
    {
        obs.Code = new CodeableConcept("http://snomed.info/sct", "1104051000000101",
            "Royal College of Physicians NEWS2 (National Early Warning Score 2) total score");
        obs.Value = new Quantity(value, "ScoreOf");
    }

    public static void SetHeartRateValues(this Observation obs, decimal heartRateBpm)
    {
        obs.Category = new List<CodeableConcept>
        {
            new("http://terminology.hl7.org/CodeSystem/observation-category", "vital-signs", "Vital Signs")
        };
        obs.Code = new CodeableConcept
        {
            Coding = new List<Coding>
            {
                new Coding("http://snomed.info/sct", "364075005", "Heart Rate"),
                new Coding("http://loinc.org", "8867-4", "Heart Rate")
            }
        };
        obs.Value = new Quantity(heartRateBpm, "/min")
        {
            Code = "/min",
        };
    }

    public static void SetBloodPressureValues(this Observation obs, decimal diastolic, decimal systolic)
    {
        obs.Category = new List<CodeableConcept>
        {
            new("http://terminology.hl7.org/CodeSystem/observation-category", "vital-signs", "Vital Signs")
        };
        obs.Code = new CodeableConcept
        {
            Coding = new List<Coding>
            {
                new Coding("http://snomed.info/sct", "75367002", "Blood pressure"),
                new Coding("http://loinc.org", "55284-4", "Blood pressure")
            }
        };
        obs.Component = new List<Observation.ComponentComponent>
        {
            new()
            {
                Code = new CodeableConcept
                {
                    Coding = new List<Coding>
                    {
                        new Coding("http://snomed.info/sct", "271650006", "Systolic blood pressure"),
                        new Coding("http://loinc.org", "8480-6", "Systolic blood pressure")
                    }
                },
                Value = new Quantity(systolic, "mmHg")
                {
                    Code = "millimeter of mercury"
                }
            },
            new()
            {
                Code = new CodeableConcept
                {
                    Coding = new List<Coding>
                    {
                        new Coding("http://snomed.info/sct", "271651007", "Diastolic blood pressure"),
                        new Coding("http://loinc.org", "8462-4", "Diastolic blood pressure")
                    }
                },
                Value = new Quantity(diastolic, "mmHg")
                {
                    Code = "millimeter of mercury",
                }
            }
        };
    }

    /// <summary>
    /// Generates a transaction bundle from a given Observation.
    /// </summary>
    /// <param name="obs"></param>
    /// <returns>A transaction bundle containing only the <see cref="obs"/> as a POST on the Observation URL</returns>
    public static Bundle ToTransactionBundle(this Observation obs)
    {
        var returnBundle = new Bundle
        {
            Type = Bundle.BundleType.Transaction,
            Id = Guid.NewGuid().ToString()
        };
        var entry = new Bundle.EntryComponent
        {
            FullUrl = $"urn:uuid:{obs.Id}",
            Resource = obs,
            Request = new Bundle.RequestComponent
            {
                Url = "Observation",
                Method = Bundle.HTTPVerb.POST
            }
        };
        returnBundle.Entry.Add(entry);
        return returnBundle;
    }
}

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
}