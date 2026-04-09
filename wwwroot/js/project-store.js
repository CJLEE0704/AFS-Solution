(function(global){
  const state={
    projects:[],
    activeProjectId:null,
    selectedMachines:[],
    pipeQueueByProject:new Map(),
    stageSlots:{},
    machSt:{},
  };

  function setProjects(projects){
    state.projects=Array.isArray(projects)?projects:[];
    rebuildQueues();
  }
  function setActiveProject(projectId){
    state.activeProjectId=projectId;
    ensureQueue(projectId);
  }
  function ensureQueue(projectId){
    if(!projectId) return [];
    if(!state.pipeQueueByProject.has(projectId)){
      const proj=state.projects.find(p=>p.id===projectId);
      const list=(proj?.pipes||[]).filter(p=>p.status!=='완료');
      state.pipeQueueByProject.set(projectId,list.map(p=>p.id));
    }
    return state.pipeQueueByProject.get(projectId)||[];
  }
  function rebuildQueues(){
    state.pipeQueueByProject.clear();
    (state.projects||[]).forEach(p=>ensureQueue(p.id));
  }
  function shiftPipe(projectId){
    const q=ensureQueue(projectId);
    return q.length?q.shift():null;
  }
  function findPipe(pipeId){
    for(const proj of state.projects){
      const p=(proj.pipes||[]).find(x=>x.id===pipeId);
      if(p) return {project:proj,pipe:p};
    }
    return null;
  }
  function setPipeStatus(pipeId,status){
    const found=findPipe(pipeId);
    if(!found) return;
    found.pipe.status=status;
    if(status==='완료'){
      const q=ensureQueue(found.project.id);
      const idx=q.indexOf(pipeId);
      if(idx>=0) q.splice(idx,1);
    }
  }

  global.ProjectStore={state,setProjects,setActiveProject,ensureQueue,shiftPipe,findPipe,setPipeStatus,rebuildQueues};
})(window);
