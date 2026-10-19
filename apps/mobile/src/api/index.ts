// Unified API surface. All screens import from this object.
import { inventoryApi } from './inventory';
import { organizationApi } from './organization';
import { marketplaceApi } from './marketplace';
import { sourcingApi } from './sourcing';
import { commerceApi } from './commerce';
import { tasksApi } from './tasks';
import { acquisitionApi } from './acquisition';
import { reportsApi } from './reports';
import { customersApi } from './customers';
import { channelsApi } from './channels';

export { ApiError, apiRequest, uploadMultipart } from './client';
export type { ApiFailureReason, CallOptions } from './client';
export { inventoryApi } from './inventory';
export { organizationApi } from './organization';
export { marketplaceApi } from './marketplace';
export { sourcingApi } from './sourcing';
export { commerceApi } from './commerce';
export { tasksApi } from './tasks';
export { acquisitionApi } from './acquisition';
export { reportsApi } from './reports';
export { customersApi } from './customers';
export { channelsApi } from './channels';

export const api = {
  // Session & dashboard
  bootstrapSession: organizationApi.bootstrapSession,
  dashboard: marketplaceApi.dashboard,

  // Organizations
  organizations: organizationApi.list,
  createOrganization: organizationApi.create,
  organizationMembers: organizationApi.members,
  addOrganizationMember: organizationApi.addMember,
  resendOrganizationMemberInvite: organizationApi.resendInvite,
  removeOrganizationMember: organizationApi.removeMember,
  updateOrganizationMemberRole: organizationApi.updateMemberRole,
  organizationPermissions: organizationApi.permissions,
  organizationRoles: organizationApi.roles,
  createOrganizationRole: organizationApi.createRole,
  updateOrganizationRole: organizationApi.updateRole,
  deleteOrganizationRole: organizationApi.deleteRole,
  organizationTeams: organizationApi.teams,
  createOrganizationTeam: organizationApi.createTeam,
  updateOrganizationTeam: organizationApi.updateTeam,
  deleteOrganizationTeam: organizationApi.deleteTeam,
  organizationTeamMembers: organizationApi.teamMembers,
  addOrganizationTeamMember: organizationApi.addTeamMember,
  removeOrganizationTeamMember: organizationApi.removeTeamMember,
  organizationInviteCodes: organizationApi.inviteCodes,
  createOrganizationInviteCode: organizationApi.createInviteCode,
  deactivateOrganizationInviteCode: organizationApi.deactivateInviteCode,
  redeemOrganizationInviteCode: organizationApi.redeemInviteCode,

  // Inventory
  inventory: inventoryApi.list,
  inventoryItem: inventoryApi.get,
  inventoryReferenceData: inventoryApi.referenceData,
  createInventory: inventoryApi.create,
  updateInventory: inventoryApi.update,
  deleteInventory: inventoryApi.archive,
  changeInventoryStatus: inventoryApi.changeStatus,
  uploadInventoryPhoto: inventoryApi.uploadPhoto,
  deleteInventoryPhoto: inventoryApi.deletePhoto,
  deleteInventoryPhotoById: inventoryApi.deletePhotoById,
  setInventoryHeroPhoto: inventoryApi.setHeroPhoto,
  inventoryIssues: inventoryApi.listIssues,
  createInventoryIssue: inventoryApi.createIssue,
  updateInventoryIssue: inventoryApi.updateIssue,
  deleteInventoryIssue: inventoryApi.deleteIssue,

  // Acquisition lots / purchases
  acquisitionLots: acquisitionApi.lots,
  acquisitionLot: acquisitionApi.lot,
  createAcquisitionLot: acquisitionApi.createLot,

  // Reports
  reportOverview: reportsApi.overview,

  // Customers / channels
  customers: customersApi.list,
  customer: customersApi.get,
  channelAccounts: channelsApi.accounts,
  channelAccount: channelsApi.account,
  createChannelAccount: channelsApi.createAccount,
  updateChannelAccount: channelsApi.updateAccount,
  deleteChannelAccount: channelsApi.deleteAccount,
  channelListings: channelsApi.listings,

  // Marketplace & Products
  createProductIdentity: marketplaceApi.createProductIdentity,
  marketplaceLinks: marketplaceApi.marketplaceLinks,
  addComp: marketplaceApi.addComp,
  comps: marketplaceApi.comps,
  generateListing: marketplaceApi.generateListing,

  // Sourcing
  trips: sourcingApi.trips,
  route: sourcingApi.route,
  tripSummary: sourcingApi.tripSummary,
  createTrip: sourcingApi.createTrip,
  deleteTrip: sourcingApi.deleteTrip,
  updateRouteStatus: sourcingApi.updateStatus,
  startRoute: sourcingApi.start,
  pauseRoute: sourcingApi.pause,
  completeRoute: sourcingApi.complete,
  createRouteStop: sourcingApi.createStop,
  updateRouteStop: sourcingApi.updateStop,
  deleteRouteStop: sourcingApi.deleteStop,
  reorderRouteStops: sourcingApi.reorderStops,
  navigateStop: sourcingApi.navigateStop,
  arriveAtStop: sourcingApi.arriveAtStop,
  completeStop: sourcingApi.completeStop,
  skipStop: sourcingApi.skipStop,
  applyStopChecklistTemplate: sourcingApi.applyChecklistTemplate,
  updateChecklistItem: sourcingApi.updateChecklistItem,
  createChecklistItem: sourcingApi.createChecklistItem,

  // Commerce
  listings: commerceApi.listings,
  createListing: commerceApi.createListing,
  updateListing: commerceApi.updateListing,
  markListingListed: commerceApi.markListingListed,
  deleteListing: commerceApi.deleteListing,
  budgetSummary: commerceApi.budgetSummary,
  currentBudget: commerceApi.currentBudget,
  createBudget: commerceApi.createBudget,
  deleteBudget: commerceApi.deleteBudget,
  addExpense: commerceApi.addExpense,
  expenses: commerceApi.expenses,
  deleteExpense: commerceApi.deleteExpense,

  // Tasks
  tasks: tasksApi.list,
  createTask: tasksApi.create,
  updateTaskStatus: tasksApi.updateStatus,
  deleteTask: tasksApi.delete,
};
