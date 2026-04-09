(function(global){
  global.DashboardUi={
    syncProjectStats(){
      if(typeof global.renderDashProjectStats==='function') global.renderDashProjectStats();
      if(typeof global.renderDashList==='function') global.renderDashList();
    }
  };
})(window);
