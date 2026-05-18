// Shield project-wide global usings. Imports that appear in 30+ files across the API.
// Keeping these here means new files don't need to remember the boilerplate and
// existing files stay focused on their feature-specific usings.

// Cross-cutting framework imports — every controller and most services need them.
global using Microsoft.AspNetCore.Authorization;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.EntityFrameworkCore;
// API-layer concerns: response contracts, auth attributes, services. The Services/ subfolders
// were moved during the cleanup pass and absorbed here so consumers didn't need to edit any
// using directives.
global using Shield.Api.Auth;
global using Shield.Api.Contracts;
global using Shield.Api.Services;
global using Shield.Api.Services.Access;
global using Shield.Api.Services.AppSettings;
global using Shield.Api.Services.Auth;
global using Shield.Api.Services.Findings;
global using Shield.Api.Services.FixApply;
global using Shield.Api.Services.Notifications;
global using Shield.Api.Services.Rendering;
global using Shield.Api.Services.Scanning;
global using Shield.Api.Services.Security;
// Shield domain types are referenced everywhere — Severity, Ecosystem, Source, Finding,
// Advisory, etc. — pulling this global keeps signatures readable.
global using Shield.Core.Abstractions;
global using Shield.Core.Domain;
global using Shield.Data;
global using Shield.Data.Identity;
