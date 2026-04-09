(function(global){
  const origSetActive=global.setActiveProject;
  global.setActiveProject=function(id){
    ProjectStore.setActiveProject(id);
    return origSetActive?.apply(this,arguments);
  };

  const origLoadProjects=global.loadProjects;
  global.loadProjects=function(){
    const ret=origLoadProjects?.apply(this,arguments);
    try{ ProjectStore.setProjects(global.projects||[]); }catch(e){}
    return ret;
  };
})(window);
